namespace PaytmReservationSystem.Models;

public class IdempotencyRecord
{
    public required string UserId { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string RequestHash { get; set; }
    public int StatusCode { get; set; }
    public required string ResponseBody { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}