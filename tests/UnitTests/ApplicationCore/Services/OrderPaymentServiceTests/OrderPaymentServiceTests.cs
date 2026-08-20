using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class OrderPaymentServiceTests
{
    private const string Buyer = "buyer@test";
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _payments = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<PaymentMethod> _paymentMethods = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderPaymentService> _logger = Substitute.For<IAppLogger<OrderPaymentService>>();
    private readonly PayPalSettings _settings = new() { Currency = "USD" };

    private OrderPaymentService NewService() =>
        new(_orders, _payments, _items, _paymentMethods, _gateway, _uri, _settings, _logger);

    private static Order BuildOrder(string buyerId = Buyer, decimal price = 50m, int units = 2)
    {
        var itemOrdered = new CatalogItemOrdered(1, "Test Product", "pic.png");
        var orderItem = new OrderItem(itemOrdered, price, units);
        return new Order(buyerId, new Address("s", "c", "st", "co", "z"), new List<OrderItem> { orderItem });
    }

    private void Arrange(Order order, Payment payment)
    {
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpec>(), Arg.Any<CancellationToken>()).Returns(payment);
    }

    [Fact]
    public async Task Authorize_ForAnotherBuyersOrder_IsRejected_AndNeverCallsPayPal()
    {
        var order = BuildOrder("owner@test");
        var payment = new Payment(1, "owner@test", "USD", order.Total());
        Arrange(order, payment);

        var instruction = new PayInstruction { Card = new CardDetails("4111111111111111", "2030-01", "123", null, null) };
        var result = await NewService().AuthorizeAsync("intruder@test", 1, instruction, default);

        Assert.False(result.IsSuccess);
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<decimal>(), Arg.Any<CardPaymentSource>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_WhenAlreadyAuthorized_IsIdempotent_AndDoesNotAuthorizeAgain()
    {
        var order = BuildOrder();
        var payment = new Payment(1, Buyer, "USD", order.Total());
        payment.MarkAuthorized("AUTH-1", "CREATED");
        Arrange(order, payment);

        var instruction = new PayInstruction { Card = new CardDetails("4111111111111111", "2030-01", "123", null, null) };
        var result = await NewService().AuthorizeAsync(Buyer, 1, instruction, default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Authorized", result.Value.Status);
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<decimal>(), Arg.Any<CardPaymentSource>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_RepeatedUnderSameKey_DoesNotRefundTwice()
    {
        var order = BuildOrder();
        var payment = Captured(order);
        payment.AddRefund("key-1", 10m, "RF-EXISTING", "COMPLETED");
        Arrange(order, payment);

        var result = await NewService().RefundAsync(Buyer, 1, 10m, "key-1", default);

        Assert.True(result.IsSuccess);
        Assert.Equal("RF-EXISTING", result.Value.PayPalRefundId);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_NewKey_CallsPayPalOnce_AndRecordsRefund()
    {
        var order = BuildOrder();
        var payment = Captured(order);
        Arrange(order, payment);
        _gateway.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult("RF-NEW", "COMPLETED", 10m));

        var result = await NewService().RefundAsync(Buyer, 1, 10m, "fresh-key", default);

        Assert.True(result.IsSuccess);
        Assert.Equal("RF-NEW", result.Value.PayPalRefundId);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        await _gateway.Received(1).RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_ExceedingCaptured_IsRejected_AndNeverCallsPayPal()
    {
        var order = BuildOrder();
        var payment = Captured(order); // captured 100
        Arrange(order, payment);

        var result = await NewService().RefundAsync(Buyer, 1, 500m, "big-refund", default);

        Assert.False(result.IsSuccess);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_WhenAuthorizationIsStale_RenewsThenCaptures()
    {
        var order = BuildOrder();
        var payment = new Payment(1, Buyer, "USD", order.Total());
        payment.MarkAuthorized("AUTH-1", "CREATED");
        Arrange(order, payment);

        _gateway.CaptureAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new PayPalException("Authorization expired", 422));
        _gateway.ReauthorizeAsync("AUTH-1", Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReauthorizeResult("AUTH-2", "CREATED"));
        _gateway.CaptureAsync("AUTH-2", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP-1", "COMPLETED", order.Total(), 3m, order.Total() - 3m, "USD"));

        var result = await NewService().FulfilAsync(1, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal("AUTH-2", payment.AuthorizationId);
        await _gateway.Received(1).ReauthorizeAsync("AUTH-1", Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_WhenAuthorizationCannotBeRenewed_ReturnsOperatorActionableError()
    {
        var order = BuildOrder();
        var payment = new Payment(1, Buyer, "USD", order.Total());
        payment.MarkAuthorized("AUTH-1", "CREATED");
        Arrange(order, payment);

        _gateway.CaptureAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new PayPalException("Authorization expired", 422));
        _gateway.ReauthorizeAsync("AUTH-1", Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new PayPalException("AUTHORIZATION_EXPIRED cannot be reauthorized", 422));

        var result = await NewService().FulfilAsync(1, default);

        Assert.False(result.IsSuccess);
        Assert.Contains("can no longer be renewed", string.Join(" ", result.Errors));
        Assert.NotEqual(PaymentStatus.Captured, payment.Status);
    }

    private static Payment Captured(Order order)
    {
        var payment = new Payment(1, order.BuyerId, "USD", 100m);
        payment.MarkAuthorized("AUTH-1", "CREATED");
        payment.MarkCaptured("CAP-1", "COMPLETED", 100m, 3m, 97m);
        return payment;
    }
}
