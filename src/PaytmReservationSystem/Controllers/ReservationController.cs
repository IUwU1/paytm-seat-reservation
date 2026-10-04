using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaytmReservationSystem.Contracts;
using PaytmReservationSystem.Data;
using PaytmReservationSystem.Infrastructure;
using PaytmReservationSystem.Models;

namespace PaytmReservationSystem.Controllers;

[ApiController]
[Route("shows/{showId}/reserve")]
[Authorize]
public class ReservationController : ControllerBase
{
    private readonly ILogger<ShowsController> _logger;
    private readonly AppDbContext _dbContext;
    
    public ReservationController(AppDbContext dbContext, ILogger<ShowsController> logger)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    [HttpPost]
    public async Task<IActionResult> ReserveSeats(Guid showId, [FromBody] ReserveSeatsRequest request)
    {
        if (request.Seats.Count == 0 || string.IsNullOrEmpty(request.IdempotencyKey))
        {
            return BadRequest(new { reason = "invalid_payload" });
        }
        
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if(string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }
        
        var sortedSeats = request.Seats.Distinct().OrderBy(s => s).ToList();
        var requestHash = ComputeSha256(string.Join(",", sortedSeats));
        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        
        var idempotency = await _dbContext.IdempotencyRecords.FirstOrDefaultAsync(i => i.UserId == userId && i.IdempotencyKey == request.IdempotencyKey) ;

        if (idempotency != null)
        {
            _logger.LogInformation("--- IDEMPOTENCY IS NOT NULL--- {Idempotency}",idempotency.RequestHash);
            if (idempotency.RequestHash == requestHash)
            {
                _logger.LogInformation("--- IDEMPOTENCY IS OK --- Cache Response for- {Hash} ",idempotency.RequestHash );
                var cachedResponse = JsonSerializer.Deserialize<ReserveSeatsResponse>(idempotency.ResponseBody);
                return Ok(cachedResponse);
            }
            MetricsRegistry.ReservationsDeclinedTotal.WithLabels("idempotentcy_key_replay").Inc();
            return Conflict(new { reason = "idempotentcy_key_replay" });
        }
        
        var lockKey = $"res_{showId}_{userId}";
        await _dbContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext({0}));", lockKey);
        
        var currentSeatCount = await _dbContext.Seats.CountAsync(s => s.ShowId == showId && s.UserId == userId && s.Status != SeatStatus.Available);
        
        if (currentSeatCount + sortedSeats.Count > 4)
        {
            MetricsRegistry.ReservationsDeclinedTotal.WithLabels("ticket_limit_exceeded").Inc();
            return Conflict(new { reason = "ticket_limit_exceeded" });
        }
        
        var show = await _dbContext.Shows.FirstOrDefaultAsync(s => s.Id == showId);
        if (show == null) return NotFound(new { reason = "show_not_found" });
        
        var reservationId = Guid.NewGuid();
        var amountPaise = show.PricePaise * sortedSeats.Count;
        var responseObj = new ReserveSeatsResponse(reservationId, showId, userId, sortedSeats, amountPaise, ReservationStatus.Confirmed);

        // var reservation = new Reservation
        // {
        //     Id = reservationId,
        //     UserId = userId,
        //     SeatCount = sortedSeats.Count,
        //     AmountPaise = amountPaise,
        //     Status = ReservationStatus.Confirmed
        // };
        //
        // _dbContext.Reservations.Add(reservation);
        
        await _dbContext.Database.ExecuteSqlRawAsync(@"
                INSERT INTO ""Reservations"" (""Id"", ""ShowId"", ""UserId"", ""SeatCount"", ""AmountPaise"", ""Status"", ""CreatedAt"")
                VALUES ({0}, {1}, {2}, {3}, {4}, {5}, NOW());",
            reservationId, showId, userId, sortedSeats.Count, amountPaise, ReservationStatus.Confirmed);
        
        

        // var idempotencyRecord = new IdempotencyRecord
        // {
        //     UserId = userId,
        //     IdempotencyKey = request.IdempotencyKey,
        //     RequestHash = requestHash,
        //     StatusCode = StatusCodes.Status201Created,
        //     ResponseBody = JsonSerializer.Serialize(responseObj)
        // };
        //
        // _dbContext.IdempotencyRecords.Add(idempotencyRecord);
        
       // await _dbContext.SaveChangesAsync();
       
       await _dbContext.Database.ExecuteSqlRawAsync(@"
                INSERT INTO ""IdempotencyRecords"" (""UserId"", ""IdempotencyKey"", ""RequestHash"", ""StatusCode"", ""ResponseBody"", ""CreatedAt"")
                VALUES ({0}, {1}, {2}, {3}, {4}, NOW());",
           userId, request.IdempotencyKey, requestHash, StatusCodes.Status201Created, JsonSerializer.Serialize(responseObj));
        
        var updatedRows = await _dbContext.Seats.Where(s =>
                s.ShowId == showId && sortedSeats.Contains(s.SeatNumber) && s.Status == SeatStatus.Available)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, SeatStatus.Confirmed)
                .SetProperty(p => p.ReservationId, reservationId)
                .SetProperty(p => p.UserId, userId)
                .SetProperty(p => p.UpdatedAt, DateTimeOffset.UtcNow));

        if (updatedRows != sortedSeats.Count)
        {
            MetricsRegistry.ReservationsDeclinedTotal.WithLabels("some_seat_taken").Inc();
            return Conflict(new { reason = "some_seat_taken" });
        }
        
        await transaction.CommitAsync();
        
        MetricsRegistry.SeatsAvailable.WithLabels(showId.ToString()).Dec(sortedSeats.Count);
        MetricsRegistry.ReservationsConfirmedTotal.Inc(sortedSeats.Count);
        
        return Created($"/shows/{showId}", responseObj);
        }catch (Exception)
        {
          
            return Conflict(new { reason = "seat_taken", message = "High contention. Please retry." });
        }
        


    }
    
    private static string ComputeSha256(string rawData)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> CancelReservation([FromRoute] Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogError("Empty user id" );
            return Unauthorized();
        }
        
        var reservation = await _dbContext.Reservations.FirstOrDefaultAsync(r =>r.Id == id);
        if (reservation == null)
        {
            _logger.LogError("Reservation not found");
            return NotFound();
        }

        if (reservation.UserId != userId)
        {
            _logger.LogError("User not authorized");
            return Forbid();
        }

        if (reservation.Status == ReservationStatus.Cancelled)
        {
            _logger.LogInformation("Reservation was cancelled");
            return Ok(new CancelReservationResponse(id, ReservationStatus.Cancelled));
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        reservation.Status = ReservationStatus.Cancelled;

        await _dbContext.Seats.Where(s => s.ReservationId == id).ExecuteUpdateAsync(s =>
            s.SetProperty(p => p.Status, SeatStatus.Available)
                .SetProperty(p => p.ReservationId, (Guid?)null)
                .SetProperty(p => p.UserId, (string?)null)
                .SetProperty(p => p.UpdatedAt, DateTimeOffset.UtcNow)
        );

        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        
        return Ok(new CancelReservationResponse(id, ReservationStatus.Cancelled));

    }
}