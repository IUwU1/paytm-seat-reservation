using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaytmReservationSystem.Contracts;
using PaytmReservationSystem.Data;
using PaytmReservationSystem.Infrastructure;
using PaytmReservationSystem.Models;

namespace PaytmReservationSystem.Controllers;

[ApiController]
[Route("[controller]")]
public class ShowsController : ControllerBase
{
    private readonly ILogger<ShowsController> _logger;
    private readonly AppDbContext _dbContext;
    
    public ShowsController(ILogger<ShowsController> logger, AppDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateShow([FromBody] CreateShowRequest request)
    {
        var distinctSeats = request.Seats.Distinct().ToList();
        if (distinctSeats.Count != request.Seats.Count)
        {
            _logger.LogError("Invalid number of seats counted seats - {DistinctSeatsCount}, request - {SeatsCount}", distinctSeats.Count, request.Seats.Count);
            return BadRequest("Duplicate seat numbers provided");
        }

        var show = new Show
        {
            Name = request.Name,
            PricePaise = request.PricePaise,
            TotalSeats = request.Seats.Count,
        };
        
        var seatEntities = distinctSeats.Select(seatNumber => new Seat
        {
            ShowId = show.Id,
            SeatNumber =  seatNumber,
            Status = SeatStatus.Available,
        }).ToList();
        
        _dbContext.Shows.Add(show);
        _dbContext.Seats.AddRange(seatEntities);
        
        await  _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Changes saved to Db Context");
        MetricsRegistry.SeatsAvailable.WithLabels(show.Id.ToString()).Set(distinctSeats.Count);
        
        return Created($"/shows/{show.Id}", new
        {
            id = show.Id,
            name = show.Name,
            total_seats = show.TotalSeats,
            price_paise = show.PricePaise
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetShow(Guid id)
    {
        var show = await _dbContext.Shows.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        
        if (show == null)
        {
            _logger.LogError("Show {ShowId} does not exist", id);
            return NotFound();
        }

        var seats = await _dbContext.Seats.AsNoTracking().Where(s => s.ShowId == show.Id).OrderBy(s => s.SeatNumber)
            .ToListAsync();
        
        Dictionary <string, int> seatDistributionCount = new Dictionary<string, int>
        {
            {SeatStatus.Available, seats.Count(s => s.Status == SeatStatus.Available)},
            { SeatStatus.Held, seats.Count(s => s.Status == SeatStatus.Held) },
            { SeatStatus.Confirmed, seats.Count(s => s.Status == SeatStatus.Confirmed) }
        };
        
        var response = new ShowResponse(
            show.Id,show.Name, show.PricePaise,show.TotalSeats,seatDistributionCount,seats.Select(s => new SeatDTO(s.SeatNumber, s.Status)).ToList());
        
        return Ok(response);
    }
}