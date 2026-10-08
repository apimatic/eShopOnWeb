using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

public class OrderPaymentServiceTests
{
    private const string Buyer = "shopper@example.com";
    private const string OtherBuyer = "someone-else@example.com";
    private const string Operator = "admin@example.com";

    private static readonly CardDetails Card = new("test_4111111145551142", "test_03", "test_2030", "test_737", "Demo Shopper");
    private static readonly Address ShipTo = new("1 Test St", "Testville", "TS", "Testland", "12345");

    private readonly string _databaseName = $"payments-{Guid.NewGuid()}";
    private readonly FakePaymentGateway _gateway = new();
    private readonly PaymentOptions _options = new() { ProviderTimeBudget = TimeSpan.FromSeconds(5) };

    public OrderPaymentServiceTests()
    {
        using var db = NewContext();
        db.CatalogBrands.Add(new CatalogBrand("Brand"));
        db.CatalogTypes.Add(new CatalogType("Type"));
        db.SaveChanges();
        db.CatalogItems.Add(new CatalogItem(1, 1, "Sweatshirt", "Sweatshirt", 19.50m, "http://catalogbaseurltobereplaced/images/products/1.png"));
        db.CatalogItems.Add(new CatalogItem(1, 1, "T-Shirt", "T-Shirt", 12m, "http://catalogbaseurltobereplaced/images/products/2.png"));
        db.SaveChanges();
    }

    private CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_databaseName).Options);

    /// <summary>One service per "request", each with its own unit of work, like the scoped registrations.</summary>
    private OrderPaymentService NewService(out CatalogContext db)
    {
        db = NewContext();
        var uriComposer = Substitute.For<IUriComposer>();
        uriComposer.ComposePicUri(Arg.Any<string>()).Returns(c => c.Arg<string>());
        return new OrderPaymentService(new EfRepository<Order>(db), new EfRepository<CatalogItem>(db), new EfPaymentStore(db), _gateway,
            uriComposer, _options, TimeProvider.System, Substitute.For<IAppLogger<OrderPaymentService>>());
    }

    private async Task<Order> PlaceOrderAsync(string buyer = Buyer)
    {
        var service = NewService(out var db);
        using (db)
        {
            // 2 x 19.50 + 1 x 12.00 = 51.00
            return await service.PlaceOrderAsync(buyer, new[] { new OrderLine(1, 2), new OrderLine(2, 1) }, ShipTo, CancellationToken.None);
        }
    }

    private async Task<PayOrderResult> PayAsync(int orderId, string buyer = Buyer)
    {
        var service = NewService(out var db);
        using (db)
        {
            return await service.PayAsync(orderId, buyer, Card, CancellationToken.None);
        }
    }

    private async Task<RefundOrderResult> RefundAsync(int orderId, decimal? amount, string? clientRequestId = null)
    {
        var service = NewService(out var db);
        using (db)
        {
            return await service.RefundAsync(orderId, amount, "RETURN", Operator, clientRequestId, CancellationToken.None);
        }
    }

    private async Task<Order> ReloadAsync(int orderId)
    {
        using var db = NewContext();
        return (await new EfRepository<Order>(db).FirstOrDefaultAsync(new ApplicationCore.Specifications.OrderWithPaymentsSpecification(orderId)))!;
    }

    [Fact]
    public async Task PlacedOrderIsPricedFromTheCatalogAndAwaitsPayment()
    {
        var order = await PlaceOrderAsync();

        Assert.Equal(51.00m, order.Total());
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus());
        Assert.Equal(Buyer, order.BuyerId);
    }

    [Fact]
    public async Task PlacingAnOrderWithAnUnknownCatalogItemIsRejected()
    {
        var service = NewService(out var db);
        using (db)
        {
            var ex = await Assert.ThrowsAsync<PaymentValidationException>(() =>
                service.PlaceOrderAsync(Buyer, new[] { new OrderLine(999, 1) }, ShipTo, CancellationToken.None));
            Assert.Contains("999", ex.Message);
        }
    }

    [Fact]
    public async Task PayingChargesTheOrderTotalToTheCentAndMarksItPaid()
    {
        var order = await PlaceOrderAsync();

        var result = await PayAsync(order.Id);

        Assert.Equal(PayOrderOutcome.Paid, result.Outcome);
        var charge = Assert.Single(_gateway.Charges);
        Assert.Equal(5100, charge.AmountMinor);
        Assert.Equal("USD", charge.Currency);
        var reloaded = await ReloadAsync(order.Id);
        Assert.Equal(OrderPaymentStatus.Paid, reloaded.PaymentStatus());
        Assert.Equal(PaymentAttemptStatus.Authorised, Assert.Single(reloaded.Payments).Status);
    }

    [Fact]
    public async Task PayingAPaidOrderAgainNeverChargesTwice()
    {
        var order = await PlaceOrderAsync();
        await PayAsync(order.Id);

        var second = await PayAsync(order.Id);

        Assert.Equal(PayOrderOutcome.AlreadyPaid, second.Outcome);
        Assert.Single(_gateway.Charges);
    }

    [Fact]
    public async Task ConcurrentDoubleClickChargesOnce()
    {
        var order = await PlaceOrderAsync();
        var release = new TaskCompletionSource();
        _gateway.OnCharge = async (request, _) =>
        {
            await release.Task;
            return FakePaymentGateway.Authorised(request);
        };

        var first = PayAsync(order.Id);
        var second = PayAsync(order.Id);
        await Task.WhenAny(Task.WhenAll(first, second), Task.Delay(500));
        release.SetResult();
        var outcomes = await Task.WhenAll(first.ContinueWith(t => t), second.ContinueWith(t => t));

        Assert.Single(_gateway.Charges);
        Assert.Single(outcomes, t => t.Status == TaskStatus.RanToCompletion && t.Result.Outcome == PayOrderOutcome.Paid);
        Assert.Single(outcomes, t => t.IsFaulted && t.Exception!.InnerException is PaymentConflictException);
    }

    [Fact]
    public async Task ClaimStoreRefusesTheSecondClaimForTheSameAttempt()
    {
        var order = await PlaceOrderAsync();
        using var first = NewContext();
        using var second = NewContext();

        await new EfPaymentStore(first).AddPaymentClaimAsync(new OrderPayment(order.Id, 1, "USD", 5100, 51m, DateTimeOffset.UtcNow), CancellationToken.None);

        await Assert.ThrowsAsync<PaymentConflictException>(() =>
            new EfPaymentStore(second).AddPaymentClaimAsync(new OrderPayment(order.Id, 1, "USD", 5100, 51m, DateTimeOffset.UtcNow), CancellationToken.None));
    }

    [Fact]
    public async Task RefusedCardLeavesTheOrderUnpaidAndTellsTheShopperWhy()
    {
        var order = await PlaceOrderAsync();
        _gateway.OnCharge = (_, _) => Task.FromResult(FakePaymentGateway.Refused("Expired Card"));

        var result = await PayAsync(order.Id);

        Assert.Equal(PayOrderOutcome.Declined, result.Outcome);
        Assert.Contains("Expired Card", result.Payment.ShopperMessage);
        Assert.Contains("You were not charged", result.Payment.ShopperMessage);
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, (await ReloadAsync(order.Id)).PaymentStatus());
    }

    [Fact]
    public async Task AfterARefusalTheShopperCanPayWithANewAttempt()
    {
        var order = await PlaceOrderAsync();
        _gateway.OnCharge = (_, _) => Task.FromResult(FakePaymentGateway.Refused("Refused"));
        await PayAsync(order.Id);
        _gateway.OnCharge = (request, _) => Task.FromResult(FakePaymentGateway.Authorised(request));

        var result = await PayAsync(order.Id);

        Assert.Equal(PayOrderOutcome.Paid, result.Outcome);
        Assert.Equal(2, result.Payment.AttemptNumber);
        var keys = _gateway.Charges.Select(c => c.IdempotencyKey).ToList();
        Assert.Equal(2, keys.Distinct().Count());
    }

    [Fact]
    public async Task ProviderRejectionReleasesTheClaim()
    {
        var order = await PlaceOrderAsync();
        _gateway.OnCharge = (_, _) => throw new PaymentGatewayException(PaymentGatewayFailure.Rejected, "Adyen rejected the request: Unable to decrypt data.");

        await Assert.ThrowsAsync<PaymentGatewayException>(() => PayAsync(order.Id));

        var reloaded = await ReloadAsync(order.Id);
        Assert.Equal(PaymentAttemptStatus.Failed, Assert.Single(reloaded.Payments).Status);
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, reloaded.PaymentStatus());
    }

    [Fact]
    public async Task UnknownOutcomeIsSettledByResendingTheSameIdempotencyKey()
    {
        var order = await PlaceOrderAsync();
        _gateway.OnCharge = (_, _) => throw new PaymentGatewayException(PaymentGatewayFailure.OutcomeUnknown, "Adyen did not respond in time.") { TimedOut = true };

        await Assert.ThrowsAsync<PaymentGatewayException>(() => PayAsync(order.Id));
        var afterTimeout = await ReloadAsync(order.Id);
        Assert.Equal(PaymentAttemptStatus.Unknown, Assert.Single(afterTimeout.Payments).Status);
        Assert.Equal(OrderPaymentStatus.PaymentPending, afterTimeout.PaymentStatus());

        _gateway.OnCharge = (request, _) => Task.FromResult(FakePaymentGateway.Authorised(request));
        var result = await PayAsync(order.Id);

        Assert.Equal(PayOrderOutcome.Paid, result.Outcome);
        Assert.Equal(1, result.Payment.AttemptNumber);
        var charges = _gateway.Charges.ToList();
        Assert.Equal(2, charges.Count);
        Assert.Equal(charges[0].IdempotencyKey, charges[1].IdempotencyKey);
        Assert.Equal(charges[0].MerchantReference, charges[1].MerchantReference);
    }

    [Fact]
    public async Task ShopperCannotPaySomeoneElsesOrder()
    {
        var order = await PlaceOrderAsync(OtherBuyer);

        await Assert.ThrowsAsync<OrderNotFoundException>(() => PayAsync(order.Id, Buyer));
        Assert.Empty(_gateway.Charges);
    }

    [Fact]
    public async Task ShoppersOnlySeeTheirOwnOrders()
    {
        var mine = await PlaceOrderAsync(Buyer);
        await PlaceOrderAsync(OtherBuyer);

        var service = NewService(out var db);
        using (db)
        {
            var orders = await service.GetOrdersForBuyerAsync(Buyer, CancellationToken.None);
            Assert.Equal(mine.Id, Assert.Single(orders).Id);
        }
    }

    [Fact]
    public async Task PartialRefundThenOverRefundIsRefused()
    {
        var order = await PlaceOrderAsync();
        await PayAsync(order.Id);

        var refund = await RefundAsync(order.Id, 12.50m);

        Assert.Equal(RefundStatus.Received, refund.Refund.Status);
        Assert.Equal(1250, Assert.Single(_gateway.Refunds).AmountMinor);
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, refund.Order.PaymentStatus());
        Assert.Equal(3850, refund.Order.RefundableMinor());

        var ex = await Assert.ThrowsAsync<PaymentConflictException>(() => RefundAsync(order.Id, 40m));
        Assert.Contains("exceed", ex.Message);
        Assert.Single(_gateway.Refunds);
    }

    [Fact]
    public async Task RefundWithoutAmountGivesBackTheRemainder()
    {
        var order = await PlaceOrderAsync();
        await PayAsync(order.Id);
        await RefundAsync(order.Id, 1m);

        var rest = await RefundAsync(order.Id, null);

        Assert.Equal(5000, rest.Refund.AmountMinor);
        Assert.Equal(OrderPaymentStatus.Refunded, (await ReloadAsync(order.Id)).PaymentStatus());
        await Assert.ThrowsAsync<PaymentConflictException>(() => RefundAsync(order.Id, 0.01m));
    }

    [Fact]
    public async Task UnpaidOrderCannotBeRefunded()
    {
        var order = await PlaceOrderAsync();

        await Assert.ThrowsAsync<PaymentConflictException>(() => RefundAsync(order.Id, 1m));
        Assert.Empty(_gateway.Refunds);
    }

    [Fact]
    public async Task ConcurrentRefundsNeverExceedWhatWasPaid()
    {
        var order = await PlaceOrderAsync();
        await PayAsync(order.Id);
        var release = new TaskCompletionSource();
        _gateway.OnRefund = async (request, _) =>
        {
            await release.Task;
            return new ProviderRefundResult($"PSP-{request.IdempotencyKey[..8]}", "received");
        };

        var first = RefundAsync(order.Id, 30m);
        var second = RefundAsync(order.Id, 30m);
        await Task.WhenAny(Task.WhenAll(first, second), Task.Delay(500));
        release.SetResult();
        var outcomes = await Task.WhenAll(first.ContinueWith(t => t), second.ContinueWith(t => t));

        Assert.Single(_gateway.Refunds);
        Assert.Single(outcomes, t => t.Status == TaskStatus.RanToCompletion);
        Assert.Single(outcomes, t => t.IsFaulted && t.Exception!.InnerException is PaymentConflictException);
        Assert.True((await ReloadAsync(order.Id)).RefundedOrClaimedMinor() <= 5100);
    }

    [Fact]
    public async Task UnknownRefundCountsAgainstThePaymentAndIsSettledBeforeTheNextRefund()
    {
        var order = await PlaceOrderAsync();
        await PayAsync(order.Id);
        _gateway.OnRefund = (_, _) => throw new PaymentGatewayException(PaymentGatewayFailure.OutcomeUnknown, "Adyen could not be reached.");

        await Assert.ThrowsAsync<PaymentGatewayException>(() => RefundAsync(order.Id, 50m));
        var afterFailure = await ReloadAsync(order.Id);
        Assert.Equal(RefundStatus.Unknown, Assert.Single(afterFailure.Refunds).Status);
        Assert.Equal(100, afterFailure.RefundableMinor());

        _gateway.OnRefund = (request, _) => Task.FromResult(new ProviderRefundResult($"PSP-{request.IdempotencyKey[..8]}", "received"));
        var next = await RefundAsync(order.Id, 1m);

        var refunds = _gateway.Refunds.ToList();
        Assert.Equal(3, refunds.Count);
        Assert.Equal(refunds[0].IdempotencyKey, refunds[1].IdempotencyKey);
        Assert.Equal(5000, refunds[1].AmountMinor);
        Assert.Equal(100, refunds[2].AmountMinor);
        var reloaded = await ReloadAsync(order.Id);
        Assert.All(reloaded.Refunds, r => Assert.Equal(RefundStatus.Received, r.Status));
        Assert.Equal(OrderPaymentStatus.Refunded, reloaded.PaymentStatus());
        Assert.Equal(next.Refund.Id, reloaded.Refunds.OrderBy(r => r.Sequence).Last().Id);
    }

    [Fact]
    public async Task OperatorIdempotencyKeyReplaysTheEarlierRefund()
    {
        var order = await PlaceOrderAsync();
        await PayAsync(order.Id);

        var first = await RefundAsync(order.Id, 5m, "operator-key-1");
        var again = await RefundAsync(order.Id, 5m, "operator-key-1");

        Assert.False(first.Replayed);
        Assert.True(again.Replayed);
        Assert.Equal(first.Refund.Id, again.Refund.Id);
        Assert.Single(_gateway.Refunds);
    }

    [Fact]
    public async Task RejectedRefundDoesNotCountAgainstThePayment()
    {
        var order = await PlaceOrderAsync();
        await PayAsync(order.Id);
        _gateway.OnRefund = (_, _) => throw new PaymentGatewayException(PaymentGatewayFailure.Rejected, "Adyen rejected the request: Invalid amount.");

        await Assert.ThrowsAsync<PaymentGatewayException>(() => RefundAsync(order.Id, 10m));

        var reloaded = await ReloadAsync(order.Id);
        Assert.Equal(RefundStatus.Rejected, Assert.Single(reloaded.Refunds).Status);
        Assert.Equal(5100, reloaded.RefundableMinor());
    }
}
