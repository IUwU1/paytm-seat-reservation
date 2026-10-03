using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PaytmReservationSystem.Security;

public class TokenAuthenticationSchemeOptions : AuthenticationSchemeOptions { }

public class TokenAuthHandler : AuthenticationHandler<TokenAuthenticationSchemeOptions>
{
    public TokenAuthHandler(IOptionsMonitor<TokenAuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock) : base(options, logger, encoder, clock)
    {
    }

    public TokenAuthHandler(IOptionsMonitor<TokenAuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            Logger.LogError("No Authorization header found");
            return Task.FromResult(AuthenticateResult.Fail("Missing Authentication Header"));
        }
        
       
        var token = authHeader.ToString().Replace("Bearer ", "").Trim();

        if (string.IsNullOrEmpty(token))
        {
            Logger.LogError("No Authorization header found");
            return Task.FromResult(AuthenticateResult.Fail("Invalid Token"));
        }

        var claims = new List<Claim>();

        if (token == "admin_secret")
        {
            Logger.LogInformation("Admin secret found");
            claims.Add(new Claim(ClaimTypes.NameIdentifier, "admin"));
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }else if (token.StartsWith("user_"))
        {
            Logger.LogInformation("User found = {Token}", token);
            claims.Add(new Claim(ClaimTypes.NameIdentifier,token));
            claims.Add(new Claim(ClaimTypes.Role,"User"));
        }
        else
        {
            return Task.FromResult(AuthenticateResult.Fail("Unauthorized Token Form"));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}