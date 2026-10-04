using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
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
        
        if (exception is TimeoutException || 
            exception.Message.Contains("Timeout") || 
            (exception.InnerException != null && exception.InnerException.Message.Contains("Timeout")))
        {
            httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await httpContext.Response.WriteAsJsonAsync(new 
            { 
                reason = "server_at_capacity",
                message = "High traffic volume. Please retry."
            }, cancellationToken);
    
            return true; 
        }

        if (exception is DbUpdateException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            await httpContext.Response.WriteAsJsonAsync(new { reason = "state_conflict" }, cancellationToken);
            return true;
        }
        
        var response = new { message = responseMessage };
        
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(response), cancellationToken);
        
        return true;
    }
}