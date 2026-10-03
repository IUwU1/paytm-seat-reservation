namespace PaytmReservationSystem.Models;

public static class SeatStatus
{
    public const string Available = "available";
    public const string Held = "held";
    public const string Confirmed = "confirmed";
}

public class Seat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShowId { get; set; }
    public required string SeatNumber { get; set; }
    public string Status { get; set; } = SeatStatus.Available;
    
    public Guid? ReservationId { get; set; }
    public string? UserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Show? Show { get; set; }
    public Reservation? Reservation { get; set; }
}