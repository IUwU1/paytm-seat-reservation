using System.Text.Json.Serialization;

namespace PaytmReservationSystem.Contracts;

public record CancelReservationResponse(
    Guid ReservationId,
    string Status
    );
    
    public record ReserveSeatsRequest(
        List<string> Seats,
         string IdempotencyKey
    );
    
    public record ReserveSeatsResponse(
         Guid ReservationId,
       Guid ShowId,
        string UserId,
        List<string> Seats,
       long AmountPaise,
         string Status
        );