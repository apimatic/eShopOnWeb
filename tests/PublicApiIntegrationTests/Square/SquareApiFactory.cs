using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.Square;

/// <summary>
/// PublicApi with test Square settings and <see cref="FakeSquare"/> as the SDK transport, so no test depends on
/// real credentials or reaches Square. With <paramref name="isolatedCatalog"/> the app gets its own catalog store.
/// </summary>
public class SquareApiFactory : WebApplicationFactory<Program>
{
    private readonly bool _isolatedCatalog;
    private readonly string _catalogName = "square-api-tests-" + Guid.NewGuid();

    public SquareApiFactory(bool isolatedCatalog = true) => _isolatedCatalog = isolatedCatalog;

    public FakeSquare Square { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(SquareHarness.TestSettings()));
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(SquareClientFactory.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Square);
            if (_isolatedCatalog)
            {
                services.RemoveAll<DbContextOptions<CatalogContext>>();
                services.AddDbContext<CatalogContext>(options => options.UseInMemoryDatabase(_catalogName));
            }
        });
    }

    public HttpClient ClientFor(string? token)
    {
        var client = CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
