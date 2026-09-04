using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderPaymentServiceTests
{
    private const string Currency = "USD";

    private readonly IRepository<Order> _orderRepository = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _catalogRepository = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<PaymentMethod> _paymentMethodRepository =
        Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();

    private OrderPaymentService BuildService() =>
        new(_orderRepository, _catalogRepository, _paymentMethodRepository, _gateway, Currency);

    private static Order BuildFulfilledOrder(decimal capturedAmount = 29m)
    {
        var order = new Order("demouser@microsoft.com",
            new Address("1 Main St", "San Jose", "CA", "US", "95131"),
            new List<OrderItem> { new OrderItem(new CatalogItemOrdered(2, "Item", "img.png"), 10m, 2) });
        order.AttachPayment(new OrderPayment(order.Id, Currency, capturedAmount));
        order.Payment.RecordAuthorization("PP-ORDER-1", "PP-AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), capturedAmount, null);
        order.Payment.RecordCapture("PP-CAPTURE-1", "COMPLETED", capturedAmount, 1.24m, capturedAmount - 1.24m);
        order.MarkFulfilled();
        return order;
    }

    private void StubOrder(Order order)
    {
        _orderRepository.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(order);
        _orderRepository.UpdateAsync(order, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task ReplyingRefundUnderSameKeyDoesNotCallGatewayTwice()
    {
        var order = BuildFulfilledOrder();
        StubOrder(order);
        _gateway.RefundAsync(Arg.Any<string>(), Arg.Any<GatewayMoney>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayRefundResult(true, "PP-REFUND-1", "COMPLETED", new GatewayMoney(5m, Currency), null));
        var service = BuildService();

        var first = await service.RefundOrderAsync(order.Id, 5m, "rf-key-1", CancellationToken.None);
        var second = await service.RefundOrderAsync(order.Id, 5m, "rf-key-1", CancellationToken.None);

        Assert.Equal(first.Refund.Id, second.Refund.Id);
        Assert.Equal(1, _gateway.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IPayPalGateway.RefundAsync)));
        Assert.Equal(5m, order.Payment!.RefundedAmount);
    }

    [Fact]
    public async Task RefundNeverExceedsCapturedAmount()
    {
        var order = BuildFulfilledOrder(29m);
        StubOrder(order);
        _gateway.RefundAsync(Arg.Any<string>(), Arg.Any<GatewayMoney>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayRefundResult(true, "PP-REFUND-1", "COMPLETED", new GatewayMoney(20m, Currency), null));
        var service = BuildService();

        await service.RefundOrderAsync(order.Id, 20m, "key-a", CancellationToken.None);
        await Assert.ThrowsAsync<OrderStateException>(
            () => service.RefundOrderAsync(order.Id, 20m, "key-b", CancellationToken.None));
    }

    [Fact]
    public async Task DistinctKeysRefundDistinctPartsofSameCapture()
    {
        var order = BuildFulfilledOrder(29m);
        StubOrder(order);
        _gateway.RefundAsync(Arg.Any<string>(), Arg.Any<GatewayMoney>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayRefundResult(true, "PP-REFUND-1", "COMPLETED", new GatewayMoney(5m, Currency), null));
        var service = BuildService();

        await service.RefundOrderAsync(order.Id, 5m, "key-1", CancellationToken.None);
        var second = await service.RefundOrderAsync(order.Id, 10m, "key-2", CancellationToken.None);

        await _gateway.Received(2).RefundAsync(Arg.Any<string>(), Arg.Any<GatewayMoney>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(15m, order.Payment.RefundedAmount);
    }

    [Fact]
    public async Task PayingAnOrderWithLiveAuthorizationDoesNotCallGatewayAgain()
    {
        var order = BuildFulfilledOrder();
        StubOrder(order);
        var service = BuildService();

        // Order is fulfilled; a second pay must be rejected rather than re-charged.
        await Assert.ThrowsAsync<OrderStateException>(
            () => service.PayOrderAsync("demouser@microsoft.com", order.Id, null, null, CancellationToken.None));
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<GatewayAuthorizeRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconciliationMatchesPayPalTransactionsToOrdersByPaymentIds()
    {
        var order = BuildFulfilledOrder(29m);
        _orderRepository.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Order> { order });
        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddHours(1);
        _gateway.SearchTransactionsAsync(from, to, Arg.Any<CancellationToken>())
            .Returns(new List<GatewayTransaction>
            {
                new GatewayTransaction("PP-CAPTURE-1", "S", new GatewayMoney(29m, Currency), to.AddMinutes(-1), "T1206", null),
                new GatewayTransaction("PP-REFUND-OTHER", "S", new GatewayMoney(-5m, Currency), to.AddMinutes(-2), "T1207", null)
            });
        var service = BuildService();

        var report = await service.ReconcileAsync(from, to, CancellationToken.None);

        Assert.Equal(2, report.TotalTransactions);
        var matched = report.Transactions.Single(t => t.TransactionId == "PP-CAPTURE-1");
        Assert.Equal(order.Id, matched.OrderId);
        Assert.Null(report.Transactions.Single(t => t.TransactionId == "PP-REFUND-OTHER").OrderId);
        Assert.Empty(report.UnmatchedOrders);
    }

    [Fact]
    public async Task ReconciliationFlagsOrdersWithNoPayPalTransaction()
    {
        var order = BuildFulfilledOrder(29m);
        _orderRepository.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Order> { order });
        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddHours(1);
        _gateway.SearchTransactionsAsync(from, to, Arg.Any<CancellationToken>())
            .Returns(new List<GatewayTransaction>());
        var service = BuildService();

        var report = await service.ReconcileAsync(from, to, CancellationToken.None);

        Assert.Empty(report.Transactions);
        Assert.Contains(report.UnmatchedOrders, o => o.OrderId == order.Id);
    }
}
