using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace PublicApiIntegrationTests.NotificationEndpoints;

/// <summary>
/// Boots the PublicApi app with the live Twilio provider swapped for a <see cref="FakeSmsProvider"/>, so
/// the SMS-notification endpoints can be exercised end-to-end without any live traffic. Each instance is a
/// fresh app with its own in-memory store.
/// </summary>
public class NotificationApiFactory : WebApplicationFactory<Program>
{
    public FakeSmsProvider Sms { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISmsProvider>();
            services.AddSingleton<ISmsProvider>(Sms);
        });
    }

    /// <summary>Mint a JWT for an arbitrary shopper identity (any validly-signed token is accepted).</summary>
    public static string TokenFor(string userName, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, userName) };
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims.ToArray()),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    public const string AdminRole = "Administrators";
}
