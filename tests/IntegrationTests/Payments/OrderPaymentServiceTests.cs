using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

/// <summary>
/// Order payment flows end to end inside the process: real service, real EF store (in-memory), real Adyen SDK
/// client — with Adyen itself scripted at the HttpClient seam. Each <see cref="NewService"/> is one request scope.
/// </summary>
public class OrderPaymentServiceTests
{
    private const string Buyer = "shopper@example.com";
    private static readonly EncryptedCard Card = new("test_4111111145551142", "test_03", "test_2030", "test_737", "Jane Shopper");

    private readonly DbContextOptions<CatalogContext> _dbOptions = new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase($"payments-{Guid.NewGuid()}")
        .Options;
    private readonly ScriptedAdyenHandler _adyen = new();
    private int _sweatshirtId;
    private int _mugId;

    public OrderPaymentServiceTests()
    {
        using var context = new CatalogContext(_dbOptions);
        var sweatshirt = new CatalogItem(1, 1, "d", "Sweatshirt", 19.50m, "http://catalogbaseurltobereplaced/images/products/1.png");
        var mug = new CatalogItem(1, 1, "d", "Mug", 8.50m, "http://catalogbaseurltobereplaced/images/products/2.png");
        context.CatalogItems.AddRange(sweatshirt, mug);
        context.SaveChanges();
        _sweatshirtId = sweatshirt.Id;
        _mugId = mug.Id;
    }

    private OrderPaymentService NewService(PaymentProcessingOptions? options = null)
    {
        var context = new CatalogContext(_dbOptions);
        return new OrderPaymentService(new OrderPaymentStore(context), AdyenTestFactory.Gateway(_adyen),
            new EfRepository<Order>(context), new EfRepository<CatalogItem>(context),
            new UriComposer(new CatalogSettings()), TimeProvider.System, options ?? new PaymentProcessingOptions(),
            Substitute.For<IAppLogger<OrderPaymentService>>());
    }

    /// <summary>2 × 19.50 + 1 × 8.50 = 47.50 USD = 4750 cents.</summary>
    private async Task<int> PlaceOrderAsync(string buyer = Buyer)
    {
        var result = await NewService().PlaceOrderAsync(buyer,
            new[] { new PlaceOrderLine(_sweatshirtId, 1), new PlaceOrderLine(_mugId, 1), new PlaceOrderLine(_sweatshirtId, 1) },
            null, CancellationToken.None);
        Assert.Equal(PlaceOrderStatus.Created, result.Status);
        return result.OrderId!.Value;
    }

    private static string AuthorisedJson(long value = 4750, string psp = "PSP-AUTH-1") =>
        $$"""{"pspReference":"{{psp}}","resultCode":"Authorised","amount":{"currency":"USD","value":{{value}}},"futureField":"x"}""";

    private async Task<Order> LoadAsync(int orderId)
    {
        await using var context = new CatalogContext(_dbOptions);
        return (await new OrderPaymentStore(context).GetOrderAsync(orderId, CancellationToken.None))!;
    }

    private async Task<int> PaidOrderAsync()
    {
        var orderId = await PlaceOrderAsync();
        _adyen.ThenJson(HttpStatusCode.OK, AuthorisedJson());
        Assert.Equal(PayOrderStatus.Paid, (await NewService().PayAsync(Buyer, orderId, Card, CancellationToken.None)).Status);
        return orderId;
    }

    [Fact]
    public async Task PlacedOrderUsesCatalogPricesAndAwaitsPayment()
    {
        var orderId = await PlaceOrderAsync();

        var order = await LoadAsync(orderId);
        Assert.Equal(47.50m, order.Total());
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Equal(Buyer, order.BuyerId);
        Assert.Equal(3, order.OrderItems.Sum(i => i.Units));
    }

    [Fact]
    public async Task OrderWithUnknownCatalogItemIsRejected()
    {
        var result = await NewService().PlaceOrderAsync(Buyer, new[] { new PlaceOrderLine(999_999, 1) }, null, CancellationToken.None);

        Assert.Equal(PlaceOrderStatus.Invalid, result.Status);
        Assert.Contains("999999", result.Message);
    }

    [Fact]
    public async Task PaymentChargesTheOrderTotalToTheCentAndKeepsAdyensResponse()
    {
        var orderId = await PlaceOrderAsync();
        _adyen.ThenJson(HttpStatusCode.OK, AuthorisedJson());

        var result = await NewService().PayAsync(Buyer, orderId, Card, CancellationToken.None);

        Assert.Equal(PayOrderStatus.Paid, result.Status);
        Assert.Contains("\"value\":4750", Assert.Single(_adyen.Sent).Body);
        var order = await LoadAsync(orderId);
        Assert.Equal(OrderPaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal("PSP-AUTH-1", order.AuthorisedPayment!.PspReference);
        Assert.Equal(4750, order.AuthorisedPayment.AmountInMinorUnits);
        Assert.Contains("futureField", Assert.Single(order.ProviderResponses).Body);
    }

    [Fact]
    public async Task PayingAPaidOrderAgainNeverChargesTwice()
    {
        var orderId = await PaidOrderAsync();

        var second = await NewService().PayAsync(Buyer, orderId, Card, CancellationToken.None);

        Assert.Equal(PayOrderStatus.AlreadyPaid, second.Status);
        Assert.Single(_adyen.Sent);
    }

    [Fact]
    public async Task DoubleClickWhileThePaymentIsInFlightReachesAdyenOnce()
    {
        var orderId = await PlaceOrderAsync();
        var release = new TaskCompletionSource();
        _adyen.Then(async (_, _) =>
        {
            await release.Task;
            return ScriptedAdyenHandler.Json(HttpStatusCode.OK, AuthorisedJson());
        });

        var firstClick = NewService().PayAsync(Buyer, orderId, Card, CancellationToken.None);
        await WaitUntilAsync(() => _adyen.Sent.Count == 1);
        var secondClick = await NewService().PayAsync(Buyer, orderId, Card, CancellationToken.None);
        release.SetResult();
        var first = await firstClick;

        Assert.Equal(PayOrderStatus.InProgress, secondClick.Status);
        Assert.Equal(PayOrderStatus.Paid, first.Status);
        Assert.Single(_adyen.Sent);
    }

    [Fact]
    public async Task RefusedCardLeavesTheOrderUnpaidAndExplainsWhy()
    {
        var orderId = await PlaceOrderAsync();
        _adyen.ThenJson(HttpStatusCode.OK, AdyenTestFactory.RefusedJson);

        var refused = await NewService().PayAsync(Buyer, orderId, Card, CancellationToken.None);

        Assert.Equal(PayOrderStatus.Refused, refused.Status);
        Assert.Contains("Not enough balance", refused.Message);
        Assert.Contains("different card", refused.Message);
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, (await LoadAsync(orderId)).PaymentStatus);

        // The shopper can then pay with another card; that is a new attempt with a new idempotency key.
        _adyen.ThenJson(HttpStatusCode.OK, AuthorisedJson());
        var paid = await NewService().PayAsync(Buyer, orderId, Card, CancellationToken.None);

        Assert.Equal(PayOrderStatus.Paid, paid.Status);
        Assert.NotEqual(_adyen.Sent[0].IdempotencyKey, _adyen.Sent[1].IdempotencyKey);
    }

    [Fact]
    public async Task UnknownPaymentOutcomeIsRecordedAndSettledWithTheSameIdempotencyKey()
    {
        var orderId = await PlaceOrderAsync();
        _adyen.ThenConnectionFailure().ThenConnectionFailure();

        var unknown = await NewService().PayAsync(Buyer, orderId, Card, CancellationToken.None);

        Assert.Equal(PayOrderStatus.ProviderDidNotRespond, unknown.Status);
        Assert.Contains("Adyen did not respond", unknown.Message);
        Assert.Equal(2, _adyen.Sent.Count); // sent, then re-sent once within the budget to settle it
        var order = await LoadAsync(orderId);
        Assert.Equal(OrderPaymentStatus.PaymentProcessing, order.PaymentStatus);
        Assert.Equal(PaymentAttemptStatus.Unknown, order.LatestPaymentAttempt!.Status);

        // The next pay call settles the unknown attempt instead of starting a fresh charge.
        _adyen.ThenJson(HttpStatusCode.OK, AuthorisedJson());
        var settled = await NewService().PayAsync(Buyer, orderId, Card, CancellationToken.None);

        Assert.Equal(PayOrderStatus.Paid, settled.Status);
        Assert.Single(_adyen.Sent.Select(s => s.IdempotencyKey).Distinct());
        order = await LoadAsync(orderId);
        var attempts = order.PaymentAttempts.OrderBy(a => a.AttemptNumber).ToList();
        Assert.Equal(PaymentAttemptStatus.Superseded, attempts[0].Status);
        Assert.Equal(PaymentAttemptStatus.Authorised, attempts[1].Status);
        Assert.Equal(1, attempts[1].SettlesAttemptNumber);
        Assert.Equal(attempts[0].Reference, attempts[1].Reference);
    }

    [Fact]
    public async Task DroppedConnectionIsSettledWithinTheSameRequest()
    {
        var orderId = await PlaceOrderAsync();
        _adyen.ThenConnectionFailure().ThenJson(HttpStatusCode.OK, AuthorisedJson());

        var result = await NewService().PayAsync(Buyer, orderId, Card, CancellationToken.None);

        Assert.Equal(PayOrderStatus.Paid, result.Status);
        Assert.Equal(2, _adyen.Sent.Count);
        Assert.Equal(_adyen.Sent[0].IdempotencyKey, _adyen.Sent[1].IdempotencyKey);
    }

    [Fact]
    public async Task HungAdyenNeverHoldsThePayRequestBeyondItsBudget()
    {
        var orderId = await PlaceOrderAsync();
        _adyen.ThenHang().ThenHang();
        var options = new PaymentProcessingOptions
        {
            RequestBudget = TimeSpan.FromSeconds(1),
            MinimumCallWindow = TimeSpan.FromMilliseconds(500)
        };
        var stopwatch = Stopwatch.StartNew();

        var result = await NewService(options).PayAsync(Buyer, orderId, Card, CancellationToken.None);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
        Assert.Equal(PayOrderStatus.ProviderDidNotRespond, result.Status);
        Assert.Equal(PaymentAttemptStatus.Unknown, (await LoadAsync(orderId)).LatestPaymentAttempt!.Status);
    }

    [Fact]
    public async Task AShopperCannotPayAnotherShoppersOrder()
    {
        var orderId = await PlaceOrderAsync();

        var result = await NewService().PayAsync("someone-else@example.com", orderId, Card, CancellationToken.None);

        Assert.Equal(PayOrderStatus.NotFound, result.Status);
        Assert.Empty(_adyen.Sent);
    }

    [Fact]
    public async Task PartialRefundsNeverExceedWhatWasPaid()
    {
        var orderId = await PaidOrderAsync();

        _adyen.ThenJson(HttpStatusCode.Created, AdyenTestFactory.RefundReceivedJson("PSP-AUTH-1", 1000));
        var partial = await NewService().RefundAsync(orderId, 10.00m, RefundReason.Return, null, CancellationToken.None);
        Assert.Equal(RefundOrderStatus.Accepted, partial.Status);
        Assert.Equal(37.50m, partial.RemainingRefundable);
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, (await LoadAsync(orderId)).PaymentStatus);
        Assert.EndsWith("/payments/PSP-AUTH-1/refunds", _adyen.Sent[^1].Uri.AbsolutePath);

        var tooMuch = await NewService().RefundAsync(orderId, 37.51m, null, null, CancellationToken.None);
        Assert.Equal(RefundOrderStatus.ExceedsRefundable, tooMuch.Status);
        Assert.Equal(2, _adyen.Sent.Count); // never sent to Adyen

        _adyen.ThenJson(HttpStatusCode.Created, AdyenTestFactory.RefundReceivedJson("PSP-AUTH-1", 3750, "PSP-REFUND-2"));
        var rest = await NewService().RefundAsync(orderId, 37.50m, null, null, CancellationToken.None);
        Assert.Equal(RefundOrderStatus.Accepted, rest.Status);
        Assert.Equal(0m, rest.RemainingRefundable);
        var order = await LoadAsync(orderId);
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(2, order.Refunds.Count);
    }

    [Fact]
    public async Task UnknownRefundOutcomeKeepsItsAmountReservedAndIsSettledByTheNextRefund()
    {
        var orderId = await PaidOrderAsync();
        _adyen.ThenConnectionFailure().ThenConnectionFailure();

        var unknown = await NewService().RefundAsync(orderId, 40m, null, null, CancellationToken.None);

        Assert.Equal(RefundOrderStatus.ProviderDidNotRespond, unknown.Status);
        Assert.Equal(7.50m, unknown.RemainingRefundable); // the 40.00 stays reserved
        var refundKey = _adyen.Sent[^1].IdempotencyKey;

        // Reserved amount still blocks over-refunding...
        _adyen.ThenJson(HttpStatusCode.Created, AdyenTestFactory.RefundReceivedJson("PSP-AUTH-1", 4000));
        var tooMuch = await NewService().RefundAsync(orderId, 10m, null, null, CancellationToken.None);

        // ...and the unknown refund was settled first, with its own idempotency key.
        Assert.Equal(RefundOrderStatus.ExceedsRefundable, tooMuch.Status);
        Assert.Equal(refundKey, _adyen.Sent[^1].IdempotencyKey);
        var order = await LoadAsync(orderId);
        Assert.Equal(RefundStatus.Received, Assert.Single(order.Refunds).Status);
    }

    [Fact]
    public async Task RetriedRefundWithTheSameKeyIsReturnedNotRepeated()
    {
        var orderId = await PaidOrderAsync();
        _adyen.ThenJson(HttpStatusCode.Created, AdyenTestFactory.RefundReceivedJson("PSP-AUTH-1", 500));

        var first = await NewService().RefundAsync(orderId, 5m, null, "operator-retry-1", CancellationToken.None);
        var retry = await NewService().RefundAsync(orderId, 5m, null, "operator-retry-1", CancellationToken.None);

        Assert.Equal(RefundOrderStatus.Accepted, first.Status);
        Assert.Equal(RefundOrderStatus.Replayed, retry.Status);
        Assert.Equal(first.RefundId, retry.RefundId);
        Assert.Equal(2, _adyen.Sent.Count); // the payment and one refund
    }

    [Fact]
    public async Task UnpaidOrderCannotBeRefunded()
    {
        var orderId = await PlaceOrderAsync();

        var result = await NewService().RefundAsync(orderId, 1m, null, null, CancellationToken.None);

        Assert.Equal(RefundOrderStatus.NotRefundable, result.Status);
        Assert.Empty(_adyen.Sent);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException();
            await Task.Delay(10);
        }
    }
}
