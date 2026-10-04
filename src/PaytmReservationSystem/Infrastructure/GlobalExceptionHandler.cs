using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace PaytmReservationSystem.Infrastructure;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }
    
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Unhandled exception caught by GlobalExceptionHandler");

        var statusCode = StatusCodes.Status500InternalServerError;
        var responseMessage = "An unexpected error occurred. Please try again later.";

        if (exception is TimeoutException or NpgsqlException)
        {
            statusCode = StatusCodes.Status429TooManyRequests;
            responseMessage = "The system is currently under heavy load. Please try again.";
        }
        
        var response = new { message = responseMessage };
        
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(response), cancellationToken);
        
        return true;
    }
}