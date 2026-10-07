using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

/// <summary>
/// The payment service over the real EF store (in-memory provider) and a scripted gateway.
/// </summary>
public class OrderPaymentServiceTests
{
    private const string Buyer = "shopper@example.com";
    private static readonly EncryptedCard Card = new("test_4111111145551142", "test_03", "test_2030", "test_737", "John Smith");

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly ScriptedGateway _gateway = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));

    private CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_databaseName).Options);

    private OrderPaymentStore NewStore() => new(NewContext(), NullLogger<OrderPaymentStore>.Instance);

    private OrderPaymentService NewService() =>
        new(NewStore(), _gateway, _clock, Substitute.For<IAppLogger<OrderPaymentService>>());

    private async Task<int> SeedOrderAsync(string buyer = Buyer, decimal unitPrice = 12.75m, int units = 4)
    {
        await using var db = NewContext();
        var order = new Order(buyer, null, new List<OrderItem>
        {
            new(new CatalogItemOrdered(1, "Mug", "http://example.com/mug.png"), unitPrice, units),
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private async Task<Order> LoadAsync(int orderId) => (await NewStore().GetAsync(orderId, includePaymentRecord: true))!;

    [Fact]
    public async Task ChargesTheOrderTotalAndRecordsAdyensResponse()
    {
        var orderId = await SeedOrderAsync();
        _gateway.Payments.Enqueue(ScriptedGateway.Authorised("PSP1"));

        var result = await NewService().PayAsync(orderId, Buyer, Card);

        Assert.Equal(PayOrderOutcome.Paid, result.Outcome);
        Assert.Equal(5100, Assert.Single(_gateway.PaymentRequests).AmountMinor);
        var order = await LoadAsync(orderId);
        Assert.Equal(OrderPaymentStatus.Paid, order.PaymentStatus);
        var entry = Assert.Single(order.PaymentRecord);
        Assert.Equal("""{"resultCode":"Authorised","pspReference":"PSP1"}""", entry.ResponseBody);
        Assert.Equal(1, entry.PaymentAttemptNumber);
    }

    [Fact]
    public async Task AnotherShoppersOrderIsNotFoundAndNeverCharged()
    {
        var orderId = await SeedOrderAsync(buyer: "someone-else@example.com");

        var result = await NewService().PayAsync(orderId, Buyer, Card);

        Assert.Equal(PayOrderOutcome.NotFound, result.Outcome);
        Assert.Empty(_gateway.PaymentRequests);
    }

    [Fact]
    public async Task PayingAgainNeverChargesAgain()
    {
        var orderId = await SeedOrderAsync();
        _gateway.Payments.Enqueue(ScriptedGateway.Authorised("PSP1"));
        await NewService().PayAsync(orderId, Buyer, Card);

        var again = await NewService().PayAsync(orderId, Buyer, Card);

        Assert.Equal(PayOrderOutcome.AlreadyPaid, again.Outcome);
        Assert.Equal(OrderPaymentStatus.Paid, again.PaymentStatus);
        Assert.Single(_gateway.PaymentRequests);
    }

    [Fact]
    public async Task DoubleClickWhileTheFirstPaymentIsInFlightIsTurnedAway()
    {
        var orderId = await SeedOrderAsync();
        var release = new TaskCompletionSource();
        _gateway.PaymentGate = release.Task;
        _gateway.Payments.Enqueue(ScriptedGateway.Authorised("PSP1"));

        var first = NewService().PayAsync(orderId, Buyer, Card);
        await _gateway.FirstPaymentEntered.Task;
        var second = await NewService().PayAsync(orderId, Buyer, Card);
        release.SetResult();

        Assert.Equal(PayOrderOutcome.InProgress, second.Outcome);
        Assert.Equal(PayOrderOutcome.Paid, (await first).Outcome);
        Assert.Single(_gateway.PaymentRequests);
    }

    [Fact]
    public async Task TwoRequestsRacingToClaimTheSamePaymentOnlyOneWins()
    {
        var orderId = await SeedOrderAsync();
        var storeA = NewStore();
        var storeB = NewStore();
        var invocationsOfA = 0;
        PaymentClaimKind? claimB = null;

        // A loads the order, then — before A saves — B loads, claims and saves. A's save must lose and A must
        // re-decide on the fresh state.
        var claimA = await storeA.UpdateAsync(orderId, order =>
        {
            if (invocationsOfA++ == 0)
            {
                claimB = storeB.UpdateAsync(orderId, o => o.ClaimPayment(5100, "USD", _clock.GetUtcNow(), OrderPaymentService.StaleClaimAfter))
                    .GetAwaiter().GetResult()!.Kind;
            }
            return order.ClaimPayment(5100, "USD", _clock.GetUtcNow(), OrderPaymentService.StaleClaimAfter);
        });

        Assert.Equal(PaymentClaimKind.Send, claimB);
        Assert.Equal(2, invocationsOfA);
        Assert.Equal(PaymentClaimKind.InProgress, claimA!.Kind);
        Assert.Single((await LoadAsync(orderId)).PaymentAttempts);
    }

    [Fact]
    public async Task UnknownOutcomeIsSettledInPlaceWithTheSameIdempotencyKey()
    {
        var orderId = await SeedOrderAsync();
        _gateway.Payments.Enqueue(ScriptedGateway.UnknownPayment());
        _gateway.Payments.Enqueue(ScriptedGateway.Authorised("PSP1"));

        var result = await NewService().PayAsync(orderId, Buyer, Card);

        Assert.Equal(PayOrderOutcome.Paid, result.Outcome);
        Assert.Equal(2, _gateway.PaymentRequests.Count);
        Assert.Single(_gateway.PaymentRequests.Select(r => r.IdempotencyKey).Distinct());
        var order = await LoadAsync(orderId);
        Assert.Equal(2, order.PaymentRecord.Count);
        Assert.Contains(order.PaymentRecord, e => e.TransportError is not null);
    }

    [Fact]
    public async Task StillUnknownOutcomeIsSettledByTheNextPayRequestWithoutANewCharge()
    {
        var orderId = await SeedOrderAsync();
        _gateway.Payments.Enqueue(ScriptedGateway.UnknownPayment());
        _gateway.Payments.Enqueue(ScriptedGateway.UnknownPayment());

        var first = await NewService().PayAsync(orderId, Buyer, Card);
        Assert.Equal(PayOrderOutcome.Unknown, first.Outcome);
        Assert.Equal(OrderPaymentStatus.PaymentPending, first.PaymentStatus);

        _gateway.Payments.Enqueue(ScriptedGateway.Authorised("PSP1"));
        var second = await NewService().PayAsync(orderId, Buyer, Card);

        Assert.Equal(PayOrderOutcome.Paid, second.Outcome);
        Assert.Single(_gateway.PaymentRequests.Select(r => r.IdempotencyKey).Distinct());
        Assert.Single((await LoadAsync(orderId)).PaymentAttempts);
    }

    [Fact]
    public async Task RefusedCardLeavesTheOrderUnpaidWithAnActionableMessage()
    {
        var orderId = await SeedOrderAsync();
        _gateway.Payments.Enqueue(new CardPaymentResult(CardPaymentOutcome.Refused, "PSP1", "Refused", "Expired Card", "6", null, null, false,
            new ProviderExchange(200, """{"resultCode":"Refused"}""", null)));

        var result = await NewService().PayAsync(orderId, Buyer, Card);

        Assert.Equal(PayOrderOutcome.Declined, result.Outcome);
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, result.PaymentStatus);
        Assert.Contains("Expired Card", result.Message);
        Assert.Contains("expiry date", result.Message);
    }

    [Fact]
    public async Task PartialRefundThenOverRefundIsRefused()
    {
        var orderId = await PaidOrderAsync();
        _gateway.Refunds.Enqueue(ScriptedGateway.RefundReceived("R1"));

        var partial = await NewService().RefundAsync(orderId, 10.50m, "damaged", null, "admin");
        var over = await NewService().RefundAsync(orderId, 40.51m, null, null, "admin");

        Assert.Equal(RefundOrderOutcome.Refunded, partial.Outcome);
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, partial.PaymentStatus);
        Assert.Equal(4050, partial.RefundableMinor);
        Assert.Equal(1050, Assert.Single(_gateway.RefundRequests).AmountMinor);
        Assert.Equal("PSP1", _gateway.RefundRequests[0].PaymentPspReference);
        Assert.Equal(RefundOrderOutcome.Invalid, over.Outcome);
        Assert.Contains("40.50", over.Message);
    }

    [Fact]
    public async Task SameOperatorKeyNeverRefundsTwice()
    {
        var orderId = await PaidOrderAsync();
        _gateway.Refunds.Enqueue(ScriptedGateway.RefundReceived("R1"));

        var first = await NewService().RefundAsync(orderId, 5m, null, "ticket-42", "admin");
        var second = await NewService().RefundAsync(orderId, 5m, null, "ticket-42", "admin");

        Assert.Equal(RefundOrderOutcome.Refunded, first.Outcome);
        Assert.Equal(RefundOrderOutcome.Existing, second.Outcome);
        Assert.Equal(first.Refund!.Id, second.Refund!.Id);
        Assert.Single(_gateway.RefundRequests);
    }

    [Fact]
    public async Task TwoRefundsRacingCannotTogetherExceedWhatWasPaid()
    {
        var orderId = await PaidOrderAsync();
        var storeA = NewStore();
        var storeB = NewStore();
        var calls = 0;

        var ex = await Assert.ThrowsAsync<OrderPaymentException>(() => storeA.UpdateAsync(orderId, order =>
        {
            if (calls++ == 0)
            {
                storeB.UpdateAsync(orderId, o => o.ClaimRefund(Guid.NewGuid(), "b", 4000, null, "admin", _clock.GetUtcNow(), OrderPaymentService.StaleClaimAfter))
                    .GetAwaiter().GetResult();
            }
            return order.ClaimRefund(Guid.NewGuid(), "a", 4000, null, "admin", _clock.GetUtcNow(), OrderPaymentService.StaleClaimAfter);
        }));

        Assert.Equal(OrderPaymentError.ExceedsRefundable, ex.Error);
        Assert.Single((await LoadAsync(orderId)).Refunds);
    }

    [Fact]
    public async Task UnknownRefundIsSettledWithItsOwnKeyBeforeTheNextRefund()
    {
        var orderId = await PaidOrderAsync();
        _gateway.Refunds.Enqueue(ScriptedGateway.UnknownRefund());
        _gateway.Refunds.Enqueue(ScriptedGateway.UnknownRefund());

        var unknown = await NewService().RefundAsync(orderId, 10m, null, null, "admin");
        Assert.Equal(RefundOrderOutcome.Unknown, unknown.Outcome);
        Assert.Equal(4100, (await LoadAsync(orderId)).RefundableMinor); // still counts against the balance

        _gateway.Refunds.Enqueue(ScriptedGateway.RefundReceived("R1"));
        _gateway.Refunds.Enqueue(ScriptedGateway.RefundReceived("R2"));
        var next = await NewService().RefundAsync(orderId, 5m, null, null, "admin");

        Assert.Equal(RefundOrderOutcome.Refunded, next.Outcome);
        Assert.Equal(4, _gateway.RefundRequests.Count);
        Assert.Equal(unknown.Refund!.IdempotencyKey, _gateway.RefundRequests[2].IdempotencyKey);
        var order = await LoadAsync(orderId);
        Assert.All(order.Refunds, r => Assert.Equal(RefundStatus.Received, r.Status));
        Assert.Equal(3600, order.RefundableMinor);
    }

    [Fact]
    public async Task RejectedRefundReleasesTheBalance()
    {
        var orderId = await PaidOrderAsync();
        _gateway.Refunds.Enqueue(new RefundResult(RefundOutcome.Rejected, null, "167", "Original pspReference required", false,
            new ProviderExchange(422, "{}", null)));

        var result = await NewService().RefundAsync(orderId, 10m, null, null, "admin");

        Assert.Equal(RefundOrderOutcome.Rejected, result.Outcome);
        Assert.Equal(OrderPaymentStatus.Paid, result.PaymentStatus);
        Assert.Equal(5100, result.RefundableMinor);
    }

    [Fact]
    public async Task UnpaidOrderCannotBeRefunded()
    {
        var orderId = await SeedOrderAsync();

        var result = await NewService().RefundAsync(orderId, null, null, null, "admin");

        Assert.Equal(RefundOrderOutcome.NotPaid, result.Outcome);
        Assert.Empty(_gateway.RefundRequests);
    }

    private async Task<int> PaidOrderAsync()
    {
        var orderId = await SeedOrderAsync();
        _gateway.Payments.Enqueue(ScriptedGateway.Authorised("PSP1"));
        Assert.Equal(PayOrderOutcome.Paid, (await NewService().PayAsync(orderId, Buyer, Card)).Outcome);
        return orderId;
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public ManualClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class ScriptedGateway : IPaymentGateway
    {
        public Queue<CardPaymentResult> Payments { get; } = new();
        public Queue<RefundResult> Refunds { get; } = new();
        public List<CardPaymentRequest> PaymentRequests { get; } = new();
        public List<RefundRequest> RefundRequests { get; } = new();
        public Task? PaymentGate { get; set; }
        public TaskCompletionSource FirstPaymentEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ProviderName => "Adyen";
        public string Currency => "USD";

        public async Task<CardPaymentResult> ChargeCardAsync(CardPaymentRequest request, CancellationToken cancellationToken = default)
        {
            lock (PaymentRequests)
                PaymentRequests.Add(request);
            FirstPaymentEntered.TrySetResult();
            if (PaymentGate is not null)
                await PaymentGate;
            return Payments.Dequeue();
        }

        public Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
        {
            RefundRequests.Add(request);
            return Task.FromResult(Refunds.Dequeue());
        }

        public static CardPaymentResult Authorised(string psp) =>
            new(CardPaymentOutcome.Authorised, psp, "Authorised", null, null, null, null, false,
                new ProviderExchange(200, $$"""{"resultCode":"Authorised","pspReference":"{{psp}}"}""", null));

        public static CardPaymentResult UnknownPayment() =>
            new(CardPaymentOutcome.Unknown, null, null, null, null, null, null, false,
                new ProviderExchange(null, null, "Connection to Adyen failed: connection reset."));

        public static RefundResult RefundReceived(string psp) =>
            new(RefundOutcome.Received, psp, null, null, false,
                new ProviderExchange(201, $$"""{"status":"received","pspReference":"{{psp}}"}""", null));

        public static RefundResult UnknownRefund() =>
            new(RefundOutcome.Unknown, null, null, null, false, new ProviderExchange(null, null, "No response from Adyen within 20 s."));
    }
}
