using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class OrderPaymentServiceTests
{
    private const string Buyer = "12345"; // OrderBuilder.TestBuyerId
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<PaymentMethod> _paymentMethods = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IPaymentOptions _options = Substitute.For<IPaymentOptions>();

    public OrderPaymentServiceTests()
    {
        _options.Currency.Returns("USD");
    }

    private Microsoft.eShopWeb.ApplicationCore.Services.OrderPaymentService NewService() =>
        new(_orders, _items, _paymentMethods, _gateway, _uriComposer, _options);

    private void OrderReturns(Order order) =>
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);

    private static Order AuthorizedOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.SetAuthorizedPayment(new Payment("PPO-1", "AUTH-1", "CREATED", order.Total(), "USD"));
        return order;
    }

    private static Order FulfilledOrder(decimal fee = 2m)
    {
        var order = AuthorizedOrder();
        var captured = order.Total();
        order.Payment!.RecordCapture("CAP-1", "COMPLETED", captured, fee, captured - fee);
        order.MarkFulfilled();
        return order;
    }

    [Fact]
    public async Task AuthorizeIsIdempotentForAnAlreadyAuthorizedOrder()
    {
        OrderReturns(AuthorizedOrder());
        var service = NewService();

        var result = await service.AuthorizeAsync(1, Buyer, new PaymentInstruction { SavedPaymentMethodId = 7 });

        Assert.Equal(OrderStatus.PaymentAuthorized, result.Status);
        // The gateway must not be hit a second time by a double-click.
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthorizeRejectsAnOrderOwnedByAnotherShopper()
    {
        OrderReturns(AuthorizedOrder()); // BuyerId = "12345"
        var service = NewService();

        await Assert.ThrowsAsync<OrderNotFoundException>(() =>
            service.AuthorizeAsync(1, "another-shopper", new PaymentInstruction { Card = SampleCard() }));
    }

    [Fact]
    public async Task FulfilRenewsAStaleAuthorizationThenCaptures()
    {
        OrderReturns(AuthorizedOrder());
        // First capture attempt fails as expired; after reauthorization the retry succeeds.
        _gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new AuthorizationExpiredException("expired"),
                _ => Task.FromResult(new CaptureResult { CaptureId = "CAP-9", Status = "COMPLETED", GrossAmount = 6.29m, PayPalFee = 0.5m, NetAmount = 5.79m, Currency = "USD" }));
        _gateway.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult { PayPalOrderId = "PPO-1", AuthorizationId = "AUTH-2", Status = "CREATED" });

        var service = NewService();
        var result = await service.FulfilAsync(1);

        Assert.Equal(OrderStatus.Fulfilled, result.Status);
        Assert.Equal("CAP-9", result.Payment!.CaptureId);
        Assert.Equal("AUTH-2", result.Payment.AuthorizationId); // renewed id persisted
        await _gateway.Received(1).ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FulfilIsIdempotentForAnAlreadyFulfilledOrder()
    {
        OrderReturns(FulfilledOrder());
        var service = NewService();

        var result = await service.FulfilAsync(1);

        Assert.Equal(OrderStatus.Fulfilled, result.Status);
        await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundWithTheSameKeyDoesNotRefundTwice()
    {
        var order = FulfilledOrder();
        order.Payment!.AddRefund("R-1", 1m, "COMPLETED", "dup-key");
        OrderReturns(order);
        var service = NewService();

        var (_, refund) = await service.RefundAsync(1, Buyer, 1m, "dup-key");

        Assert.Equal("R-1", refund.RefundId);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundBeyondCapturedIsRejectedBeforeHittingTheGateway()
    {
        OrderReturns(FulfilledOrder());
        var service = NewService();

        await Assert.ThrowsAsync<InvalidOrderStateException>(() =>
            service.RefundAsync(1, Buyer, 9999m, "some-key"));
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundRequiresAFulfilledOrder()
    {
        OrderReturns(AuthorizedOrder()); // authorized, not captured
        var service = NewService();

        await Assert.ThrowsAsync<InvalidOrderStateException>(() =>
            service.RefundAsync(1, Buyer, null, "some-key"));
    }

    [Fact]
    public async Task CancelVoidsHeldFundsAndMarksCancelled()
    {
        OrderReturns(AuthorizedOrder());
        var service = NewService();

        var result = await service.CancelAsync(1);

        Assert.Equal(OrderStatus.Cancelled, result.Status);
        await _gateway.Received(1).VoidAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>());
    }

    private static CardDetails SampleCard() =>
        new("4111111111111111", "12", "2030", "123", "Test Shopper", null);
}
