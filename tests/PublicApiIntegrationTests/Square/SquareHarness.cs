using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.Square;

/// <summary>
/// The Square integration exactly as the app registers it, over an isolated in-memory database and
/// <see cref="FakeSquare"/> as the SDK's HTTP transport. Nothing leaves the process.
/// </summary>
public sealed class SquareHarness : IAsyncDisposable
{
    public const string ConfiguredToken = "configured-access-token";
    public const string ApplicationId = "sandbox-app-id";
    public const string ApplicationSecret = "sandbox-app-secret";
    public const string RedirectUri = "https://shop.test/api/square/callback";

    public SquareHarness(Action<Dictionary<string, string?>>? configure = null)
    {
        Square.Now = Clock.GetUtcNow;
        var settings = TestSettings();
        configure?.Invoke(settings);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var databaseName = "square-tests-" + Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<CatalogContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IUriComposer>(new UriComposer(new CatalogSettings()));
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSquareIntegration(configuration);
        services.AddHttpClient(SquareClientFactory.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Square);
        Services = services.BuildServiceProvider(validateScopes: true);
    }

    public static Dictionary<string, string?> TestSettings() => new()
    {
        ["Square:Environment"] = "sandbox",
        ["Square:ApplicationId"] = ApplicationId,
        ["Square:ApplicationSecret"] = ApplicationSecret,
        ["Square:RedirectUri"] = RedirectUri,
        ["Square:AccessToken"] = ConfiguredToken,
    };

    public FakeSquare Square { get; } = new();
    public ManualClock Clock { get; } = new();
    public ServiceProvider Services { get; }

    public async Task<T> Run<TService, T>(Func<TService, Task<T>> action) where TService : notnull
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public async Task<T> Db<T>(Func<CatalogContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<CatalogContext>());
    }

    public async Task<List<CatalogItem>> SeedCatalogAsync(params (string Name, decimal Price)[] items)
    {
        return await Db(async db =>
        {
            var created = new List<CatalogItem>();
            foreach (var (name, price) in items)
            {
                var item = new CatalogItem(1, 1, name + " description", name, price, "images/products/1.png");
                db.CatalogItems.Add(item);
                created.Add(item);
            }
            await db.SaveChangesAsync();
            return created;
        });
    }

    public ValueTask DisposeAsync() => Services.DisposeAsync();
}

/// <summary>A clock tests can move; timers stay real.</summary>
public sealed class ManualClock : TimeProvider
{
    private TimeSpan _offset;
    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + _offset;
    public void Advance(TimeSpan by) => _offset += by;
}
