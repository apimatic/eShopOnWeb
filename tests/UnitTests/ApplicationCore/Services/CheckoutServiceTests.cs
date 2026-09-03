using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class CheckoutServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<SavedPaymentMethod> _methods = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly PayPalOptions _options = new()
    {
        ClientId = "test-client-id",
        ClientSecret = "test-client-secret",
        Currency = "USD"
    };

    private CheckoutService CreateSut() =>
        new(_orders, _items, _methods, _uriComposer, _gateway, _options);

    [Fact]
    public async Task PayDoesNotAuthorizeTwice()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("paypal-order", "auth-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "USD");
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await CreateSut().PayAsync(
            new PayOrderCommand(1, new OrderBuilder().TestBuyerId,
                new CardPaymentDetails("4111111111111111", "2030-01", "123", null, null),
                null),
            CancellationToken.None);

        Assert.Equal("auth-1", result.PayPalAuthorizationId);
        await _gateway.DidNotReceiveWithAnyArgs().AuthorizeAsync(default, default!, default, default!, default, default, default);
    }

    [Fact]
    public async Task RefundSameKeyDoesNotCallGatewayAgain()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("paypal-order", "auth-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "USD");
        order.RecordCapture("cap-1", "COMPLETED", 3.69m, 0.41m, 3.28m, "USD");
        order.RecordRefund("rf-1", "COMPLETED", 1.00m, "USD", "same-key");
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(order);

        var outcome = await CreateSut().RefundAsync(
            new RefundOrderCommand(1, new OrderBuilder().TestBuyerId, 1.00m, "same-key"),
            CancellationToken.None);

        Assert.Equal("rf-1", outcome.Refund.PayPalRefundId);
        await _gateway.DidNotReceiveWithAnyArgs().RefundAsync(default!, default, default!, default!, default);
    }
}
