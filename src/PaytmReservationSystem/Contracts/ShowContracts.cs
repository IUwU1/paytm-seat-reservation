using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PaytmReservationSystem.Contracts;

public record CreateShowRequest
(
    [Required]
    [property: JsonPropertyName("name")]
    string Name,
    [Required, MinLength(1)]
    [property: JsonPropertyName("seats")]
    List<string> Seats,
    [Range(1, long.MaxValue)]
    [property: JsonPropertyName("price_paise")]
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