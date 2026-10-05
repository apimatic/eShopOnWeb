using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Logging;
using Microsoft.eShopWeb.Infrastructure.Payments.PayPal;
using Microsoft.eShopWeb.PaymentTests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

/// <summary>
/// The production payment registration (<see cref="PayPalServiceCollectionExtensions.AddPayPalPayments"/>) wired to
/// an in-memory database and the <see cref="FakePayPal"/> handler — no network.
/// </summary>
public sealed class PaymentTestHost : IDisposable
{
    public const string Buyer = "shopper@example.com";
    public const string OtherBuyer = "someone-else@example.com";
    public const string BaseUrl = "https://paypal.test.invalid";

    private readonly ServiceProvider _provider;

    public PaymentTestHost(Action<Dictionary<string, string?>>? configure = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["PayPal:ClientId"] = "test-client-id",
            ["PayPal:ClientSecret"] = "test-client-secret",
            ["PayPal:Environment"] = "sandbox",
            ["PayPal:Currency"] = "USD",
            ["PayPal:BaseUrl"] = BaseUrl,
            ["PayPal:RequestBudget"] = "00:00:05",
            ["PayPal:AttemptTimeout"] = "00:00:02",
        };
        configure?.Invoke(settings);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(new ListLoggerProvider(Logs)).SetMinimumLevel(LogLevel.Trace));
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
        services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
        services.AddSingleton<TimeProvider>(Clock);
        services.AddPayPalPayments(configuration);
        services.AddHttpClient(PayPalServiceCollectionExtensions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => PayPal);
        _provider = services.BuildServiceProvider(validateScopes: true);
    }

    public FakePayPal PayPal { get; } = new();
    public ManualClock Clock { get; } = new();
    public ConcurrentQueue<string> Logs { get; } = new();

    /// <summary>A fresh DI scope = one request: its own DbContext and its own PayPal time budget.</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = _provider.CreateScope();
        return await work(scope.ServiceProvider);
    }

    public Task InScopeAsync(Func<IServiceProvider, Task> work) => InScopeAsync(async sp => { await work(sp); return true; });

    public T Get<T>(IServiceProvider sp) where T : notnull => sp.GetRequiredService<T>();

    /// <summary>Places an order (2 × 19.50 + 1 × 12.00 = 51.00) directly in the store.</summary>
    public Task<int> PlaceOrderAsync(string buyer = Buyer, decimal unitPrice = 19.50m, int units = 2) => InScopeAsync(async sp =>
    {
        var db = sp.GetRequiredService<CatalogContext>();
        var order = new Order(buyer, new Address("1 Main St", "Seattle", "WA", "US", "98101"),
            new List<OrderItem>
            {
                new(new CatalogItemOrdered(1, "Sweatshirt", "http://catalog/1.png"), unitPrice, units),
                new(new CatalogItemOrdered(3, "T-Shirt", "http://catalog/3.png"), 12.00m, 1)
            });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    });

    public void Dispose() => _provider.Dispose();
}

/// <summary>A clock tests can move forward; timers still run on real time.</summary>
public sealed class ManualClock : TimeProvider
{
    private TimeSpan _offset;
    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + _offset;
    public void Advance(TimeSpan by) => _offset += by;
}

public sealed class ListLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _sink;
    public ListLoggerProvider(ConcurrentQueue<string> sink) => _sink = sink;
    public ILogger CreateLogger(string categoryName) => new ListLogger(_sink, categoryName);
    public void Dispose() { }

    private sealed class ListLogger : ILogger
    {
        private readonly ConcurrentQueue<string> _sink;
        private readonly string _category;
        public ListLogger(ConcurrentQueue<string> sink, string category) { _sink = sink; _category = category; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _sink.Enqueue($"{logLevel} {_category}: {formatter(state, exception)} {exception}");
    }
}
