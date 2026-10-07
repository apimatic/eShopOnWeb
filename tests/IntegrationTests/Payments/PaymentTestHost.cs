using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

/// <summary>
/// The payment service wired as in production (EF repositories, the EF claim store, scoped contexts) over
/// an isolated in-memory store, with a scripted payment gateway in place of Adyen.
/// </summary>
public sealed class PaymentTestHost : IDisposable
{
    public const string Shopper = "shopper@example.com";
    public const string OtherShopper = "other@example.com";
    public const string Operator = "admin@example.com";

    private readonly ServiceProvider _provider;

    public PaymentTestHost()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        // One provider per test host: EF's many-internal-providers warning is expected here.
        services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase(databaseName, databaseRoot)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddLogging();
        services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
        services.AddSingleton<IUriComposer>(new UriComposer(new CatalogSettings()));
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IPaymentGateway>(Gateway);
        services.AddScoped<IPaymentOperationLock, EfPaymentOperationLockStore>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        db.CatalogItems.Add(new CatalogItem(1, 1, "Sweatshirt", "Sweatshirt", 19.50m, "1.png"));
        db.CatalogItems.Add(new CatalogItem(1, 1, "T-Shirt", "T-Shirt", 12.00m, "2.png"));
        db.SaveChanges();
    }

    public TestClock Clock { get; } = new();
    public ScriptedPaymentGateway Gateway { get; } = new();

    public IServiceScope CreateScope() => _provider.CreateScope();

    /// <summary>Runs <paramref name="action"/> in its own DI scope, like one HTTP request.</summary>
    public async Task<T> Request<T>(Func<IOrderPaymentService, Task<T>> action)
    {
        using var scope = _provider.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IOrderPaymentService>());
    }

    /// <summary>Places a 51.00 order (2 × 19.50 + 1 × 12.00) for <paramref name="buyerId"/>.</summary>
    public async Task<int> PlaceOrder(string buyerId = Shopper)
    {
        var result = await Request(s => s.PlaceOrderAsync(buyerId, new[] { new OrderLine(1, 2), new OrderLine(2, 1) }, null, CancellationToken.None));
        return result.Order!.Id;
    }

    public static EncryptedCardDetails Card { get; } = new("test_4111111145551142", "test_03", "test_2030", "test_737", "Test Shopper");

    public void Dispose() => _provider.Dispose();
}

public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>A payment gateway whose answers each test scripts, recording every call it receives.</summary>
public sealed class ScriptedPaymentGateway : IPaymentGateway
{
    private readonly ConcurrentQueue<Func<PaymentAuthorisationRequest, Task<PaymentAuthorisationResult>>> _payments = new();
    private readonly ConcurrentQueue<Func<GatewayRefundRequest, Task<RefundResult>>> _refunds = new();

    public string Currency => "USD";
    public ConcurrentQueue<PaymentAuthorisationRequest> PaymentCalls { get; } = new();
    public ConcurrentQueue<GatewayRefundRequest> RefundCalls { get; } = new();

    public void NextPayment(Func<PaymentAuthorisationRequest, PaymentAuthorisationResult> answer) => _payments.Enqueue(r => Task.FromResult(answer(r)));
    public void NextPayment(Func<PaymentAuthorisationRequest, Task<PaymentAuthorisationResult>> answer) => _payments.Enqueue(answer);
    public void NextRefund(Func<GatewayRefundRequest, RefundResult> answer) => _refunds.Enqueue(r => Task.FromResult(answer(r)));

    public Task<PaymentAuthorisationResult> AuthoriseAsync(PaymentAuthorisationRequest request, CancellationToken cancellationToken)
    {
        PaymentCalls.Enqueue(request);
        return _payments.TryDequeue(out var answer) ? answer(request) : Task.FromResult(Authorised(request));
    }

    public Task<RefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken)
    {
        RefundCalls.Enqueue(request);
        return _refunds.TryDequeue(out var answer) ? answer(request) : Task.FromResult(Received(request));
    }

    public static PaymentAuthorisationResult Authorised(PaymentAuthorisationRequest r) =>
        new(PaymentAuthorisationOutcome.Authorised, "PSP-" + r.IdempotencyKey[..8], "Authorised", null, null, r.AmountMinor, null, null, false,
            new[] { new ProviderExchange(DateTimeOffset.UtcNow, 200, $$"""{"pspReference":"PSP-{{r.IdempotencyKey[..8]}}","resultCode":"Authorised","newField":1}""") });

    public static PaymentAuthorisationResult Refused(PaymentAuthorisationRequest r) =>
        new(PaymentAuthorisationOutcome.Refused, "PSP-REFUSED", "Refused", "Not enough balance", "51", null, null, null, false,
            new[] { new ProviderExchange(DateTimeOffset.UtcNow, 200, """{"resultCode":"Refused","refusalReason":"Not enough balance"}""") });

    public static PaymentAuthorisationResult NoAnswer(PaymentAuthorisationRequest r) =>
        new(PaymentAuthorisationOutcome.Unknown, null, null, null, null, null, null, null, true, Array.Empty<ProviderExchange>());

    public static RefundResult Received(GatewayRefundRequest r) =>
        new(RefundOutcome.Received, "REF-" + r.IdempotencyKey[..8], null, null, false,
            new[] { new ProviderExchange(DateTimeOffset.UtcNow, 201, """{"status":"received"}""") });

    public static RefundResult NoAnswer(GatewayRefundRequest r) =>
        new(RefundOutcome.Unknown, null, null, null, true, Array.Empty<ProviderExchange>());
}
