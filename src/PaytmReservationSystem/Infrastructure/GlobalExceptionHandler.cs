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
        _logger.LogError(exception, "Unhandled exception GlobalExceptionHandler");

        //check errors then tackle 409s...
        var response = new { message = exception.Message, excep = exception.ToString() };
        var statusCode = StatusCodes.Status409Conflict;

        if (exception is TimeoutException or NpgsqlException)
        {
            statusCode = StatusCodes.Status429TooManyRequests;
            response = new { message = exception.Message, excep = exception.ToString() };
        }
        
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(response), cancellationToken);
        
        _logger.LogError(exception, "Unhandled exception GlobalExceptionHandler");

        return true;
    }
}