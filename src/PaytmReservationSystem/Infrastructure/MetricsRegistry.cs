using Prometheus;

namespace PaytmReservationSystem.Infrastructure;

public static class MetricsRegistry
{
    public static readonly Counter ReservationsConfirmedTotal = Metrics.CreateCounter(
        "paytm_reservations_confirmed_total",
        "Total number of successful seat reservations confirmed."
    );
    
    public static readonly Counter ReservationsDeclinedTotal = Metrics.CreateCounter(
        "paytm_reservations_declined_total",
        "Total number of seat reservations declined, categorized by reason.",
        new CounterConfiguration { LabelNames = new[] { "reason" } }
    );
    
    public static readonly Gauge SeatsAvailable = Metrics.CreateGauge(
        "paytm_seats_available",
        "Current number of available seats for a given show.",
        new GaugeConfiguration { LabelNames = new[] { "show_id" } }
    );
}