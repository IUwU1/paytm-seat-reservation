using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaytmReservationSystem.Contracts;
using PaytmReservationSystem.Data;
using PaytmReservationSystem.Models;

namespace PaytmReservationSystem.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public class ReservationController : ControllerBase
{
    private readonly ILogger<ShowController> _logger;
    private readonly AppDbContext _dbContext;
    
    public ReservationController(AppDbContext dbContext, ILogger<ShowController> logger)
    {
        _logger = logger;
        _dbContext = dbContext;
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