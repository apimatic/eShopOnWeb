using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Square.Models.Enums;

namespace PublicApiIntegrationTests.SquareIntegration;

[TestClass]
public class SquareBuildingBlockTests
{
    private static SquareSettings ValidSettings() => new()
    {
        Environment = "sandbox",
        ApplicationId = "app-id",
        ApplicationSecret = "app-secret",
        RedirectUri = "https://shop.example/api/square/callback",
    };

    [TestMethod]
    public void ValidSettingsPassAndAccessTokenIsOptional()
    {
        var result = new SquareSettingsValidator().Validate(null, ValidSettings());
        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void EachMissingRequiredSettingIsNamedWithoutEchoingValues()
    {
        var settings = ValidSettings();
        settings.ApplicationSecret = "   ";
        settings.ApplicationId = null;
        settings.RedirectUri = "not-a-url";
        settings.Environment = "staging";

        var result = new SquareSettingsValidator().Validate(null, settings);

        Assert.IsTrue(result.Failed);
        var message = string.Join(" | ", result.Failures!);
        StringAssert.Contains(message, "Square:ApplicationSecret");
        StringAssert.Contains(message, "Square:ApplicationId");
        StringAssert.Contains(message, "Square:RedirectUri");
        StringAssert.Contains(message, "Square:Environment");
        Assert.IsFalse(message.Contains("not-a-url") || message.Contains("staging"), "values are never echoed");
    }

    [TestMethod]
    public void ProductionAndSandboxSelectTheMatchingSquareEnvironment()
    {
        var settings = ValidSettings();
        Assert.AreEqual(global::Square.Servers.ServerEnvironment.Sandbox, settings.ServerEnvironment);
        settings.Environment = "Production";
        Assert.AreEqual(global::Square.Servers.ServerEnvironment.Production, settings.ServerEnvironment);
    }

    [TestMethod]
    public async Task HostRefusesToStartWithoutRequiredSquareSettings()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Square:Environment"] = "sandbox",
            ["Square:ApplicationId"] = "app-id",
            ["Square:RedirectUri"] = "https://shop.example/api/square/callback",
        });
        builder.Services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase("settings-" + Guid.NewGuid()));
        builder.Services.AddSquareIntegration(builder.Configuration);
        using var host = builder.Build();

        var ex = await Assert.ThrowsExceptionAsync<OptionsValidationException>(() => host.StartAsync());
        StringAssert.Contains(ex.Message, "Square:ApplicationSecret");
    }

    [TestMethod]
    public void MoneyUsesTheCurrencysSmallestUnit()
    {
        Assert.AreEqual(1950L, SquareMoney.ToMinorUnits(19.50m, Currency.Usd));
        Assert.AreEqual(1950L, SquareMoney.ToMinorUnits(1950m, Currency.Jpy));
        Assert.AreEqual(19500L, SquareMoney.ToMinorUnits(19.5m, Currency.Kwd));
        Assert.AreEqual(1L, SquareMoney.ToMinorUnits(0.005m, Currency.Usd));
        var money = SquareMoney.From(8.50m, Currency.Usd);
        Assert.AreEqual(850L, money.Amount);
        Assert.AreEqual("USD", money.Currency!.Value);
        Assert.IsTrue(SquareMoney.Matches(money, 8.5m, Currency.Usd));
        Assert.IsFalse(SquareMoney.Matches(money, 8.51m, Currency.Usd));
        Assert.IsFalse(SquareMoney.Matches(money, 8.5m, Currency.Cad));
        Assert.IsFalse(SquareMoney.Matches(null, 8.5m, Currency.Usd));
    }

    [TestMethod]
    public void PhotoFormatIsDetectedFromTheFileSignature()
    {
        Assert.AreEqual(SquareImageFormat.Jpeg, SquarePhotoService.DetectFormat(new byte[] { 0xFF, 0xD8, 0xFF, 0xDB }));
        Assert.AreEqual(SquareImageFormat.Png, SquarePhotoService.DetectFormat(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 }));
        Assert.IsNull(SquarePhotoService.DetectFormat("GIF89a"u8.ToArray()));
        Assert.IsNull(SquarePhotoService.DetectFormat("%PDF-1.7"u8.ToArray()));
        Assert.IsNull(SquarePhotoService.DetectFormat(Array.Empty<byte>()));
    }

    [TestMethod]
    public async Task TheStoreRefusesASecondClaimWithTheSameKey()
    {
        var services = new ServiceCollection();
        var name = "claims-" + Guid.NewGuid();
        services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase(name));
        await using var provider = services.BuildServiceProvider();

        using (var first = provider.CreateScope())
        {
            var db = first.ServiceProvider.GetRequiredService<CatalogContext>();
            db.SquareLeases.Add(new SquareLease { Name = "catalog-sync", Owner = "a", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) });
            await db.SaveChangesAsync();
        }

        using var second = provider.CreateScope();
        var db2 = second.ServiceProvider.GetRequiredService<CatalogContext>();
        db2.SquareLeases.Add(new SquareLease { Name = "catalog-sync", Owner = "b", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) });
        var ex = await Assert.ThrowsExceptionAsync<Exception>(async () =>
        {
            try
            {
                await db2.SaveChangesAsync();
            }
            catch (Exception inner)
            {
                throw new Exception("wrapped", inner);
            }
        });

        Assert.IsTrue(SquareLeaseStore.IsDuplicateKey(ex.InnerException!), ex.InnerException!.GetType().FullName + ": " + ex.InnerException.Message);
    }

    [TestMethod]
    public async Task LeaseIsExclusiveUntilReleased()
    {
        var services = new ServiceCollection();
        var name = "leases-" + Guid.NewGuid();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase(name));
        services.AddSingleton<SquareLeaseStore>();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<SquareLeaseStore>();

        var first = await store.TryAcquireAsync("photo:1", TimeSpan.FromMinutes(1), default);
        var second = await store.TryAcquireAsync("photo:1", TimeSpan.FromMinutes(1), default);
        var other = await store.TryAcquireAsync("photo:2", TimeSpan.FromMinutes(1), default);

        Assert.IsNotNull(first);
        Assert.IsNull(second);
        Assert.IsNotNull(other);

        await first!.DisposeAsync();
        var third = await store.TryAcquireAsync("photo:1", TimeSpan.FromMinutes(1), default);
        Assert.IsNotNull(third);
    }

    [TestMethod]
    public async Task ConcurrentClaimsLetExactlyOneThrough()
    {
        var services = new ServiceCollection();
        var name = "race-" + Guid.NewGuid();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase(name));
        services.AddSingleton<SquareLeaseStore>();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<SquareLeaseStore>();

        var results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => store.TryAcquireAsync("catalog-sync", TimeSpan.FromMinutes(1), default))));

        Assert.AreEqual(1, results.Count(r => r is not null));
    }
}
