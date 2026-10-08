using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

public class OrderPaymentServiceTests
{
    private const string Buyer = "shopper@example.com";
    private const string OtherBuyer = "someone-else@example.com";
    private static readonly EncryptedCard Card = new("test_4111111145551142", "test_03", "test_2030", "test_737", "Jane Shopper");

    private readonly string _databaseName = $"payments-{Guid.NewGuid():N}";
    private readonly FakeGateway _gateway = new();

    private CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_databaseName).Options);

    private OrderPaymentService NewService(CatalogContext? context = null, IPaymentGateway? gateway = null)
    {
        context ??= NewContext();
        return new OrderPaymentService(
            new EfRepository<Order>(context),
            new EfRepository<CatalogItem>(context),
            new EfRepository<PaymentAttempt>(context),
            new EfRepository<OrderRefund>(context),
            new PaymentStateStore(context),
            gateway ?? _gateway,
            new UriComposer(new CatalogSettings()),
            Substitute.For<IAppLogger<OrderPaymentService>>());
    }

    private async Task<(int CheapId, int DearId)> SeedCatalogAsync()
    {
        await using var context = NewContext();
        var cheap = new CatalogItem(1, 1, "Mug", ".NET Mug", 8.50m, "mug.png");
        var dear = new CatalogItem(1, 1, "Hoodie", ".NET Hoodie", 19.99m, "hoodie.png");
        context.CatalogItems.AddRange(cheap, dear);
        await context.SaveChangesAsync();
        return (cheap.Id, dear.Id);
    }

    private async Task<Order> PlaceOrderAsync(string buyer = Buyer)
    {
        var (cheap, dear) = await SeedCatalogAsync();
        var result = await NewService().PlaceOrderAsync(buyer, new[] { new PlaceOrderLine(cheap, 2), new PlaceOrderLine(dear, 1) }, null);
        Assert.Equal(PlaceOrderOutcome.Created, result.Outcome);
        return result.Order!;
    }

    private async Task<Order> PlacePaidOrderAsync()
    {
        var order = await PlaceOrderAsync();
        _gateway.Charges.Enqueue((_, _) => Task.FromResult(Authorised("PSPPAID000000001")));
        var paid = await NewService().PayOrderAsync(new PayOrderCommand(order.Id, Buyer, Card, "https://localhost/return"));
        Assert.Equal(PayOrderOutcome.Paid, paid.Outcome);
        return paid.Order!;
    }

    private async Task<Order> ReloadAsync(int orderId)
    {
        await using var context = NewContext();
        return await context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
    }

    private static ChargeResult Authorised(string psp) => new(ChargeStatus.Authorised, "ok", psp, "Authorised");
    private static RefundResult Received(string psp) => new(RefundGatewayStatus.Received, "ok", psp);

    [Fact]
    public async Task Place_order_prices_items_from_catalog_and_awaits_payment()
    {
        var order = await PlaceOrderAsync();

        Assert.Equal(36.99m, order.Total());
        Assert.Equal("USD", order.Currency);
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
    }

    [Fact]
    public async Task Place_order_rejects_unknown_catalog_items()
    {
        await SeedCatalogAsync();

        var result = await NewService().PlaceOrderAsync(Buyer, new[] { new PlaceOrderLine(9999, 1) }, null);

        Assert.Equal(PlaceOrderOutcome.Invalid, result.Outcome);
        Assert.Contains("9999", result.Message);
    }

    [Fact]
    public async Task Pay_charges_the_order_total_to_the_cent_and_marks_it_paid()
    {
        var order = await PlaceOrderAsync();
        _gateway.Charges.Enqueue((_, _) => Task.FromResult(Authorised("PSP0000000000001")));

        var result = await NewService().PayOrderAsync(new PayOrderCommand(order.Id, Buyer, Card, "https://localhost/return"));

        Assert.Equal(PayOrderOutcome.Paid, result.Outcome);
        var charge = Assert.Single(_gateway.ChargeCalls);
        Assert.Equal(3699, charge.AmountMinorUnits);
        Assert.Equal("USD", charge.Currency);
        var stored = await ReloadAsync(order.Id);
        Assert.Equal(OrderPaymentStatus.Paid, stored.PaymentStatus);
        Assert.Equal(36.99m, stored.AmountPaid);
        Assert.Equal("PSP0000000000001", stored.PaymentPspReference);
    }

    [Fact]
    public async Task Paying_again_after_payment_never_charges_twice()
    {
        var order = await PlacePaidOrderAsync();

        var again = await NewService().PayOrderAsync(new PayOrderCommand(order.Id, Buyer, Card, "https://localhost/return"));

        Assert.Equal(PayOrderOutcome.AlreadyPaid, again.Outcome);
        Assert.Single(_gateway.ChargeCalls);
    }

    [Fact]
    public async Task Concurrent_double_click_reaches_adyen_once()
    {
        var order = await PlaceOrderAsync();
        var release = new TaskCompletionSource();
        _gateway.Charges.Enqueue(async (_, _) => { await release.Task; return Authorised("PSP0000000000009"); });

        var first = NewService().PayOrderAsync(new PayOrderCommand(order.Id, Buyer, Card, "https://localhost/return"));
        await _gateway.FirstChargeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = await NewService().PayOrderAsync(new PayOrderCommand(order.Id, Buyer, Card, "https://localhost/return"));
        release.SetResult();
        var firstResult = await first;

        Assert.Equal(PayOrderOutcome.Paid, firstResult.Outcome);
        Assert.Equal(PayOrderOutcome.PaymentInProgress, second.Outcome);
        Assert.Single(_gateway.ChargeCalls);
    }

    [Fact]
    public async Task Racing_claims_for_the_same_attempt_are_refused_by_the_store()
    {
        var order = await PlaceOrderAsync();
        await using var a = NewContext();
        await using var b = NewContext();

        var claimA = await new PaymentStateStore(a).TryClaimAsync(new PaymentAttempt(order.Id, 1, 3699, 36.99m, "USD", DateTimeOffset.UtcNow), CancellationToken.None);
        var claimB = await new PaymentStateStore(b).TryClaimAsync(new PaymentAttempt(order.Id, 1, 3699, 36.99m, "USD", DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.True(claimA);
        Assert.False(claimB);
    }

    [Fact]
    public async Task Declined_card_leaves_order_unpaid_and_next_attempt_uses_a_new_key()
    {
        var order = await PlaceOrderAsync();
        _gateway.Charges.Enqueue((_, _) => Task.FromResult(new ChargeResult(ChargeStatus.Declined, "Your card was declined (Not enough balance).", "PSPREFUSED000001", "Refused", "Not enough balance", "51")));
        _gateway.Charges.Enqueue((_, _) => Task.FromResult(Authorised("PSP0000000000002")));

        var declined = await NewService().PayOrderAsync(new PayOrderCommand(order.Id, Buyer, Card, "https://localhost/return"));
        Assert.Equal(PayOrderOutcome.Declined, declined.Outcome);
        Assert.Contains("Not enough balance", declined.Message);
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, (await ReloadAsync(order.Id)).PaymentStatus);

        var retry = await NewService().PayOrderAsync(new PayOrderCommand(order.Id, Buyer, Card, "https://localhost/return"));
        Assert.Equal(PayOrderOutcome.Paid, retry.Outcome);
        Assert.NotEqual(_gateway.ChargeCalls[0].IdempotencyKey, _gateway.ChargeCalls[1].IdempotencyKey);
    }

    [Fact]
    public async Task Unknown_attempt_is_settled_with_same_key_on_next_pay()
    {
        var order = await PlaceOrderAsync();
        _gateway.Charges.Enqueue((_, _) => Task.FromResult(new ChargeResult(ChargeStatus.Unknown, "Adyen did not respond in time.", TimedOut: true)));
        _gateway.Charges.Enqueue((_, _) => Task.FromResult(Authorised("PSP0000000000003")));

        var timedOut = await NewService().PayOrderAsync(new PayOrderCommand(order.Id, Buyer, Card, "https://localhost/return"));
        Assert.Equal(PayOrderOutcome.ProcessorTimeout, timedOut.Outcome);
        Assert.Equal(OrderPaymentStatus.PaymentPending, (await ReloadAsync(order.Id)).PaymentStatus);

        var settled = await NewService().PayOrderAsync(new PayOrderCommand(order.Id, Buyer, Card, "https://localhost/return"));

        Assert.Equal(PayOrderOutcome.Paid, settled.Outcome);
        Assert.Equal(2, _gateway.ChargeCalls.Count);
        Assert.Equal(_gateway.ChargeCalls[0].IdempotencyKey, _gateway.ChargeCalls[1].IdempotencyKey);
        Assert.Equal(_gateway.ChargeCalls[0].AmountMinorUnits, _gateway.ChargeCalls[1].AmountMinorUnits);
    }

    [Fact]
    public async Task Another_shoppers_order_is_not_found()
    {
        var order = await PlaceOrderAsync();

        var result = await NewService().PayOrderAsync(new PayOrderCommand(order.Id, OtherBuyer, Card, "https://localhost/return"));

        Assert.Equal(PayOrderOutcome.OrderNotFound, result.Outcome);
        Assert.Empty(_gateway.ChargeCalls);
        Assert.Empty(await NewService().ListBuyerOrdersAsync(OtherBuyer));
    }

    [Fact]
    public async Task Partial_refunds_never_exceed_what_was_paid()
    {
        var order = await PlacePaidOrderAsync();
        _gateway.Refunds.Enqueue((_, _) => Task.FromResult(Received("RFND000000000001")));
        _gateway.Refunds.Enqueue((_, _) => Task.FromResult(Received("RFND000000000002")));

        var partial = await NewService().RefundOrderAsync(new RefundOrderCommand(order.Id, 10.00m, "damaged", null, "admin"));
        Assert.Equal(RefundOrderOutcome.Submitted, partial.Outcome);
        Assert.Equal(1000, _gateway.RefundCalls[0].AmountMinorUnits);
        Assert.Equal("PSPPAID000000001", _gateway.RefundCalls[0].PaymentPspReference);
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, (await ReloadAsync(order.Id)).PaymentStatus);

        var tooMuch = await NewService().RefundOrderAsync(new RefundOrderCommand(order.Id, 27.00m, null, null, "admin"));
        Assert.Equal(RefundOrderOutcome.ExceedsRefundable, tooMuch.Outcome);
        Assert.Single(_gateway.RefundCalls);

        var rest = await NewService().RefundOrderAsync(new RefundOrderCommand(order.Id, null, null, null, "admin"));
        Assert.Equal(RefundOrderOutcome.Submitted, rest.Outcome);
        Assert.Equal(2699, _gateway.RefundCalls[1].AmountMinorUnits);
        var stored = await ReloadAsync(order.Id);
        Assert.Equal(OrderPaymentStatus.Refunded, stored.PaymentStatus);
        Assert.Equal(36.99m, stored.AmountRefunded);
    }

    [Fact]
    public async Task Refund_of_unpaid_order_is_refused()
    {
        var order = await PlaceOrderAsync();

        var result = await NewService().RefundOrderAsync(new RefundOrderCommand(order.Id, 1m, null, null, "admin"));

        Assert.Equal(RefundOrderOutcome.OrderNotPaid, result.Outcome);
        Assert.Empty(_gateway.RefundCalls);
    }

    [Fact]
    public async Task Refund_with_same_idempotency_key_is_not_repeated()
    {
        var order = await PlacePaidOrderAsync();
        _gateway.Refunds.Enqueue((_, _) => Task.FromResult(Received("RFND000000000003")));

        var first = await NewService().RefundOrderAsync(new RefundOrderCommand(order.Id, 5m, null, "key-1", "admin"));
        var second = await NewService().RefundOrderAsync(new RefundOrderCommand(order.Id, 5m, null, "key-1", "admin"));
        var reused = await NewService().RefundOrderAsync(new RefundOrderCommand(order.Id, 6m, null, "key-1", "admin"));

        Assert.Equal(RefundOrderOutcome.Submitted, first.Outcome);
        Assert.Equal(RefundOrderOutcome.AlreadySubmitted, second.Outcome);
        Assert.Equal(first.Refund!.Id, second.Refund!.Id);
        Assert.Equal(RefundOrderOutcome.IdempotencyKeyReused, reused.Outcome);
        Assert.Single(_gateway.RefundCalls);
    }

    [Fact]
    public async Task Concurrent_refunds_cannot_together_exceed_what_was_paid()
    {
        var order = await PlacePaidOrderAsync();
        var release = new TaskCompletionSource();
        _gateway.Refunds.Enqueue(async (_, _) => { await release.Task; return Received("RFND000000000004"); });
        _gateway.Refunds.Enqueue(async (_, _) => { await release.Task; return Received("RFND000000000005"); });

        var first = NewService().RefundOrderAsync(new RefundOrderCommand(order.Id, 30m, null, null, "admin"));
        await _gateway.FirstRefundStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = await NewService().RefundOrderAsync(new RefundOrderCommand(order.Id, 30m, null, null, "admin"));
        release.SetResult();

        Assert.Equal(RefundOrderOutcome.Submitted, (await first).Outcome);
        Assert.Equal(RefundOrderOutcome.ExceedsRefundable, second.Outcome);
        Assert.Single(_gateway.RefundCalls);
        Assert.Equal(30m, (await ReloadAsync(order.Id)).AmountRefunded);
    }

    [Fact]
    public async Task Concurrent_reservation_conflict_is_detected_by_the_concurrency_stamp()
    {
        var order = await PlacePaidOrderAsync();
        await using var a = NewContext();
        await using var b = NewContext();
        var orderA = await a.Orders.SingleAsync(o => o.Id == order.Id);
        var orderB = await b.Orders.SingleAsync(o => o.Id == order.Id);

        orderA.ReserveRefund(30m);
        orderB.ReserveRefund(30m);

        Assert.True(await new PaymentStateStore(a).TrySaveOrderAsync(orderA, CancellationToken.None));
        Assert.False(await new PaymentStateStore(b).TrySaveOrderAsync(orderB, CancellationToken.None));
        Assert.Equal(30m, orderB.AmountRefunded); // refreshed from the store
    }

    [Fact]
    public async Task Rejected_refund_releases_its_reservation()
    {
        var order = await PlacePaidOrderAsync();
        _gateway.Refunds.Enqueue((_, _) => Task.FromResult(new RefundResult(RefundGatewayStatus.Rejected, "Adyen rejected the refund.")));

        var result = await NewService().RefundOrderAsync(new RefundOrderCommand(order.Id, 5m, null, null, "admin"));

        Assert.Equal(RefundOrderOutcome.Rejected, result.Outcome);
        var stored = await ReloadAsync(order.Id);
        Assert.Equal(0m, stored.AmountRefunded);
        Assert.Equal(OrderPaymentStatus.Paid, stored.PaymentStatus);
    }

    [Fact]
    public async Task Unknown_refund_is_settled_before_next_refund()
    {
        var order = await PlacePaidOrderAsync();
        _gateway.Refunds.Enqueue((_, _) => Task.FromResult(new RefundResult(RefundGatewayStatus.Unknown, "Adyen did not respond in time.", TimedOut: true)));
        _gateway.Refunds.Enqueue((_, _) => Task.FromResult(Received("RFND000000000006"))); // settle resend
        _gateway.Refunds.Enqueue((_, _) => Task.FromResult(Received("RFND000000000007"))); // new refund

        var unknown = await NewService().RefundOrderAsync(new RefundOrderCommand(order.Id, 30m, null, null, "admin"));
        Assert.Equal(RefundOrderOutcome.ProcessorTimeout, unknown.Outcome);
        Assert.Equal(30m, (await ReloadAsync(order.Id)).AmountRefunded); // still reserved

        var tooMuch = await NewService().RefundOrderAsync(new RefundOrderCommand(order.Id, 10m, null, null, "admin"));
        Assert.Equal(RefundOrderOutcome.ExceedsRefundable, tooMuch.Outcome);
        Assert.Equal(_gateway.RefundCalls[0].IdempotencyKey, _gateway.RefundCalls[1].IdempotencyKey);

        var next = await NewService().RefundOrderAsync(new RefundOrderCommand(order.Id, 6.99m, null, null, "admin"));
        Assert.Equal(RefundOrderOutcome.Submitted, next.Outcome);
        Assert.Equal(3, _gateway.RefundCalls.Count);

        var refunds = (await NewService().ListBuyerOrdersAsync(Buyer)).Single().Refunds;
        Assert.All(refunds, r => Assert.Equal(RefundStatus.Received, r.Status));
    }

    [Fact]
    public async Task My_orders_lists_only_the_callers_orders_with_payment_state()
    {
        var paid = await PlacePaidOrderAsync();
        var (cheap, _) = await SeedCatalogAsync();
        await NewService().PlaceOrderAsync(OtherBuyer, new[] { new PlaceOrderLine(cheap, 1) }, null);

        var mine = await NewService().ListBuyerOrdersAsync(Buyer);

        var view = Assert.Single(mine);
        Assert.Equal(paid.Id, view.Order.Id);
        Assert.Equal(OrderPaymentStatus.Paid, view.Order.PaymentStatus);
        Assert.Single(view.Attempts);
    }

    private sealed class FakeGateway : IPaymentGateway
    {
        public string Currency => "USD";
        public ConcurrentQueue<Func<ChargeCommand, CancellationToken, Task<ChargeResult>>> Charges { get; } = new();
        public ConcurrentQueue<Func<RefundCommand, CancellationToken, Task<RefundResult>>> Refunds { get; } = new();
        public List<ChargeCommand> ChargeCalls { get; } = new();
        public List<RefundCommand> RefundCalls { get; } = new();
        public TaskCompletionSource FirstChargeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstRefundStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ChargeResult> ChargeAsync(ChargeCommand command, CancellationToken cancellationToken)
        {
            lock (ChargeCalls) ChargeCalls.Add(command);
            FirstChargeStarted.TrySetResult();
            return Charges.TryDequeue(out var next) ? next(command, cancellationToken) : throw new InvalidOperationException("Unexpected charge");
        }

        public Task<RefundResult> RefundAsync(RefundCommand command, CancellationToken cancellationToken)
        {
            lock (RefundCalls) RefundCalls.Add(command);
            FirstRefundStarted.TrySetResult();
            return Refunds.TryDequeue(out var next) ? next(command, cancellationToken) : throw new InvalidOperationException("Unexpected refund");
        }
    }
}
