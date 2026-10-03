namespace PaytmReservationSystem.Models;

public class Show
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; } 
    public long PricePaise { get; set; }
    public int TotalSeats { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Seat> Seats { get; set; } = new List<Seat>();
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}