using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>PublicApi with Maxio replaced by <see cref="FakeMaxio"/> and a subscription store of its own.</summary>
public sealed class MaxioTestApplication : WebApplicationFactory<Program>
{
    private readonly Action<MaxioSettings>? _configure;

    public MaxioTestApplication(Action<MaxioSettings>? configure = null) => _configure = configure;

    public FakeMaxio Maxio { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(MaxioBillingServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Maxio);
            services.PostConfigure<MaxioSettings>(settings =>
            {
                settings.BaseUrl = "https://maxio.fake";
                settings.PlanCacheSeconds = 0;
                _configure?.Invoke(settings);
            });

            var databaseName = "subscriptions-" + Guid.NewGuid();
            services.RemoveAll<DbContextOptions<CatalogContext>>();
            services.AddDbContext<CatalogContext>(options => options.UseInMemoryDatabase(databaseName));
        });
    }

    public HttpClient ClientFor(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
