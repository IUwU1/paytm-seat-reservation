namespace PaytmReservationSystem.Models;

public static class ReservationStatus
{
    public const string Confirmed = "confirmed";
    public const string Cancelled = "cancelled";
}
public class Reservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShowId { get; set; }
    public required string UserId { get; set; }
    public int SeatCount { get; set; }
    public long AmountPaise { get; set; }
    public string Status { get; set; } = ReservationStatus.Confirmed;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Show? Show { get; set; }
    public ICollection<Seat> Seats { get; set; } = new List<Seat>(); 
}