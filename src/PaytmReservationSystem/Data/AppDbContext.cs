using Microsoft.EntityFrameworkCore;
using PaytmReservationSystem.Models;

namespace PaytmReservationSystem.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    
    public DbSet<Show> Shows => Set<Show>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<Show>(s =>
        {
            s.HasKey(h => h.Id);
            s.Property(h => h.Name).HasMaxLength(255).IsRequired();
            s.Property(h => h.PricePaise).IsRequired();
        });

        modelBuilder.Entity<Seat>(s =>
        {
            s.HasKey(h => h.Id);
            s.Property(h =>h.SeatNumber).HasMaxLength(32).IsRequired();
            s.Property(h => h.Status).HasMaxLength(32).IsRequired();
            s.Property(h => h.UserId).HasMaxLength(128).IsRequired();

            s.HasIndex(h => new { h.ShowId, h.SeatNumber }).IsUnique();
            s.HasIndex(h => new { h.ShowId, h.UserId, h.Status }).IsUnique();
            
            s.HasOne(h => h.Show).WithMany(show =>show.Seats).HasForeignKey(h =>h.ShowId).OnDelete(DeleteBehavior.Cascade);
            s.HasOne(h =>h.Reservation).WithMany(rsv => rsv.Seats).HasForeignKey(h=>h.ReservationId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Reservation>(r =>
        {
            r.HasKey(h => h.Id);
            r.Property(h => h.UserId).HasMaxLength(128).IsRequired();
            r.Property(h => h.Status).HasMaxLength(32).IsRequired();
            
            r.HasOne(a => a.Show).WithMany(show =>show.Reservations).HasForeignKey(a => a.ShowId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IdempotencyRecord>(s =>
        {
            s.HasKey(h => new { h.UserId, h.IdempotencyKey });
            s.Property(h => h.IdempotencyKey).HasMaxLength(255).IsRequired();
            s.Property(h => h.UserId).HasMaxLength(128).IsRequired();
            s.Property(h => h.RequestHash).HasMaxLength(64).IsRequired();
            s.Property(h => h.ResponseBody).HasColumnType("text").IsRequired();
        });
    }

}