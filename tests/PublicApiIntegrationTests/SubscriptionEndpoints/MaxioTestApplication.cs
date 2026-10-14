using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// PublicApi with Maxio replaced by <see cref="FakeMaxio"/> and its own catalog database, so claims from one test
/// never leak into another and no test talks to a real Maxio site.
/// </summary>
public sealed class MaxioTestApplication : WebApplicationFactory<Program>
{
    private readonly FakeMaxio _maxio;
    private readonly string _databaseName = $"SubscriptionTests-{Guid.NewGuid()}";

    public MaxioTestApplication(FakeMaxio maxio)
    {
        _maxio = maxio;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestServerResponseBodyFilter.AddTo(builder);
        builder.UseSetting("UseOnlyInMemoryDatabase", "true");
        // Placeholder values: the fake handler answers every call, nothing leaves the process.
        builder.UseSetting("Maxio:ApiKey", "test-api-key");
        builder.UseSetting("Maxio:BaseUrl", "https://maxio.test");
        builder.UseSetting("Maxio:ProductFamilyHandle", _maxio.FamilyHandle);
        builder.UseSetting("Maxio:PlanCacheSeconds", "0");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<CatalogContext>>();
            services.AddDbContext<CatalogContext>(options => options.UseInMemoryDatabase(_databaseName));

            services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => _maxio);
        });
    }

    public HttpClient ClientFor(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
