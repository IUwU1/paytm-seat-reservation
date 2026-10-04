using Microsoft.AspNetCore.Authorization;
using Serilog;
using Microsoft.EntityFrameworkCore;
using PaytmReservationSystem.Data;
using PaytmReservationSystem.Infrastructure;
using PaytmReservationSystem.Security;
using Prometheus;

Log.Logger = new  LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();
    
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter()));
    
    
    var connectionString = builder.Configuration.GetConnectionString("defaultConnection");
    
 builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions => 
    {
        npgsqlOptions.MaxBatchSize(100); 
    }));
    
    builder.Services.AddHealthChecks().AddNpgSql(connectionString,name: "postgres instance",tags: new [] { "ready"});
    
    builder.Services.AddAuthentication("Bearer").AddScheme<TokenAuthenticationSchemeOptions,TokenAuthHandler >("Bearer", null);

    builder.Services.AddAuthorization();
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    var app = builder.Build();

    app.UseExceptionHandler();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        Log.Information("Migration successful");
    }

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    
    app.UseHttpsRedirection();
    app.UseSerilogRequestLogging();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    app.UseHttpMetrics();
    app.MapMetrics().AllowAnonymous();
    
    app.MapHealthChecks("/health/liveness", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false 
    }).AllowAnonymous();
    
    app.MapHealthChecks("/health/readiness", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready") 
    }).AllowAnonymous();
    
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "something went wrong at app startup...");
}
finally
{
    Log.CloseAndFlush();
}