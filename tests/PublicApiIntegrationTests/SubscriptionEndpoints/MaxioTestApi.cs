using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>PublicApi with the Maxio SDK's HttpClient pointed at a <see cref="FakeMaxio"/>.</summary>
public sealed class MaxioTestApi : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings;

    public MaxioTestApi(Dictionary<string, string?>? settings = null)
    {
        _settings = new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-placeholder-not-a-real-key",
            ["Maxio:BaseUrl"] = "https://maxio.test.invalid",
            ["Maxio:Subdomain"] = "",
            ["Maxio:ProductFamilyHandle"] = "test-family",
            ["Maxio:PlanCacheSeconds"] = "0"
        };
        foreach (var (key, value) in settings ?? new())
        {
            _settings[key] = value;
        }
    }

    public FakeMaxio Maxio { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(_settings));
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(MaxioBillingServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Maxio);
        });
    }

    /// <summary>Creates a fresh eShopOnWeb user and returns a client carrying that user's JWT.</summary>
    public async Task<(HttpClient Client, string UserName)> NewShopperAsync()
    {
        var userName = $"shopper-{Guid.NewGuid():N}@example.com";
        using (var scope = Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var result = await users.CreateAsync(new ApplicationUser { UserName = userName, Email = userName }, AuthorizationConstants.DEFAULT_PASSWORD);
            if (!result.Succeeded) throw new InvalidOperationException("Could not create test user.");
        }

        string token;
        using (var scope = Services.CreateScope())
        {
            token = await scope.ServiceProvider.GetRequiredService<ITokenClaimsService>().GetTokenAsync(userName);
        }

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, userName);
    }
}
