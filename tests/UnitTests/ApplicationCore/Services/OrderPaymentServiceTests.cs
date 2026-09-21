using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderPaymentServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IReadRepository<CatalogItem> _catalogItems = Substitute.For<IReadRepository<CatalogItem>>();
    private readonly IReadRepository<SavedPaymentMethod> _savedCards = Substitute.For<IReadRepository<SavedPaymentMethod>>();
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderPaymentService> _logger = Substitute.For<IAppLogger<OrderPaymentService>>();

    private OrderPaymentService CreateService() => new(
        _orders, _catalogItems, _savedCards, _gateway, _uriComposer, new PerOrderLock(), new PaymentRunContext(), _logger);

    private static Order AuthorizedOrder()
    {
        var items = new List<OrderItem> { new(new CatalogItemOrdered(1, "Item", "p.png"), 50m, 1) };
        var order = new Order("buyer-1", new Address("s", "c", "st", "US", "00000"), items);
        order.MarkAuthorized(new OrderPayment("PPO", "AUTH", "CREATED", 50m, "USD", null));
        return order;
    }

    private static Order CapturedOrder()
    {
        var order = AuthorizedOrder();
        order.Payment!.RecordCapture("CAP", "COMPLETED", 50m, 2m, 48m);
        order.MarkFulfilled();
        return order;
    }

    [Fact]
    public async Task PayAsync_WhenAlreadyAuthorized_IsIdempotent_DoesNotCallGateway()
    {
        _gateway.Currency.Returns("USD");
        _orders.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(AuthorizedOrder());
        var service = CreateService();

        var result = await service.PayAsync("buyer-1", 1, new CardDetails("4111111111111111", "2027-12", "123", "N", null), null, default);

        Assert.Equal(OrderStatus.Authorized, result.Status);
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<AuthorizeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundAsync_RepeatIdempotencyKey_ReturnsStoredRefund_DoesNotCallGateway()
    {
        var order = CapturedOrder();
        var first = order.Payment!.AddRefund("PPREFUND", 10m, "COMPLETED", "key-1");
        _orders.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(order);
        var service = CreateService();

        var outcome = await service.RefundAsync("buyer-1", 1, 10m, "key-1", default);

        Assert.Same(first, outcome.Refund);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundAsync_BeyondRemaining_Throws_DoesNotCallGateway()
    {
        var order = CapturedOrder(); // captured 50
        _orders.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(order);
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentOperationException>(
            () => service.RefundAsync("buyer-1", 1, 60m, "key-x", default));
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundAsync_MissingIdempotencyKey_Throws()
    {
        var order = CapturedOrder();
        _orders.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(order);
        var service = CreateService();

        await Assert.ThrowsAnyAsync<System.Exception>(() => service.RefundAsync("buyer-1", 1, 10m, "", default));
    }
}
