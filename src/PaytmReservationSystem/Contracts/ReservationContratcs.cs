namespace PaytmReservationSystem.Contracts;

public record CancelReservationResponse(
    Guid ReservationId,
    string Status
    );