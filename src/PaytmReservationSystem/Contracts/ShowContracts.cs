using System.ComponentModel.DataAnnotations;

namespace PaytmReservationSystem.Contracts;

public record CreateShowRequest
(
    [Required]
    string Name,
    [Required, MinLength(1)]
    List<string> Seats,
    [Range(1, long.MaxValue)]
    long PricePaise
);

public record ShowResponse(
        Guid Id,
        string Name,
        long PricePaise,
        int TotalSeats,
        Dictionary<string, int> SeatCounts,
        List<SeatDTO> Seats);
        
public record SeatDTO
(
    string SeatNumber,
        string Status
);