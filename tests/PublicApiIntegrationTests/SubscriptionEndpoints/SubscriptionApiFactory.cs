using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>Non-secret placeholder Maxio settings so the host starts without real credentials.</summary>
public static class MaxioTestSettings
{
    public static readonly Dictionary<string, string?> Values = new()
    {
        ["UseOnlyInMemoryDatabase"] = "true",
        ["Maxio:ApiKey"] = "test-api-key",
        ["Maxio:Subdomain"] = "test-site",
        ["Maxio:ProductFamilyHandle"] = FakeMaxio.FamilyHandle,
        ["Maxio:BaseUrl"] = "https://maxio.test",
        ["Maxio:Environment"] = "US",
        ["Maxio:PlanCacheSeconds"] = "0"
    };

    public static void Apply(IWebHostBuilder builder)
    {
        foreach (var (key, value) in Values)
        {
            builder.UseSetting(key, value);
        }
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Values));
        builder.ConfigureServices(services => services.AddTransient<IStartupFilter, TestServerPipeWriterFix>());
    }
}

/// <summary>PublicApi host whose Maxio client talks to a <see cref="FakeMaxio"/> instead of the network.</summary>
public class SubscriptionApiFactory : WebApplicationFactory<Program>
{
    public FakeMaxio Maxio { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        MaxioTestSettings.Apply(builder);
        builder.ConfigureTestServices(services =>
            services.AddHttpClient(MaxioServiceCollectionExtensions.HTTP_CLIENT_NAME)
                .ConfigurePrimaryHttpMessageHandler(() => Maxio));
    }

    /// <summary>A client authenticated as a fresh, unique shopper (the in-memory store is shared across hosts).</summary>
    public HttpClient ClientFor(string userName)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(userName));
        return client;
    }

    public static string NewUserName() => $"shopper-{Guid.NewGuid():N}@example.com";

    private static string Token(string userName)
    {
        var key = Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, userName) }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
