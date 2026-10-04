using System.Text.Json.Serialization;

namespace PaytmReservationSystem.Contracts;

public record CancelReservationResponse(
    Guid ReservationId,
    string Status
    );
    
    public record ReserveSeatsRequest(
        [property: JsonPropertyName("seats")] List<string> Seats,
        [property: JsonPropertyName("idempotency_key")] string IdempotencyKey
    );
    
    public record ReserveSeatsResponse(
        [property: JsonPropertyName("reservation_id")] Guid ReservationId,
        [property: JsonPropertyName("show_id")] Guid ShowId,
        [property: JsonPropertyName("user_id")] string UserId,
        [property: JsonPropertyName("seats")] List<string> Seats,
        [property: JsonPropertyName("amount_paise")] long AmountPaise,
        [property: JsonPropertyName("status")] string Status
        );