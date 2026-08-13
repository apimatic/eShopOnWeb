using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace PublicApiIntegrationTests.SmsNotifications;

/// <summary>
/// A PublicApi test host with the Twilio-backed <see cref="ISmsProvider"/> swapped for a
/// <see cref="FakeSmsProvider"/>, so the SMS notification endpoints can be driven end to end in memory.
/// </summary>
public class SmsNotificationApp : WebApplicationFactory<Program>
{
    public FakeSmsProvider Sms { get; } = new();

    private readonly string _dbSuffix = Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISmsProvider>();
            services.AddSingleton<ISmsProvider>(Sms);

            // Give each test host its own in-memory stores so no state leaks between tests.
            services.RemoveAll<DbContextOptions<CatalogContext>>();
            services.RemoveAll<DbContextOptions<AppIdentityDbContext>>();
            services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase("catalog-" + _dbSuffix));
            services.AddDbContext<AppIdentityDbContext>(o => o.UseInMemoryDatabase("identity-" + _dbSuffix));
        });
    }

    public HttpClient ClientFor(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static string AdminToken() => TokenFor("admin@microsoft.com", "Administrators");
    public static string ShopperToken(string userName) => TokenFor(userName);

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
}
