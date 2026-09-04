using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Settings;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services;

public class OrderPaymentServiceTests
{
    private readonly CatalogContext _context;
    private readonly EfRepository<Order> _orderRepository;
    private readonly EfRepository<OrderRefund> _refundRepository;
    private readonly EfRepository<CatalogItem> _catalogItemRepository;
    private readonly EfRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _gateway;

    public OrderPaymentServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName: "TestPayments_" + Guid.NewGuid().ToString("N"))
            .Options;
        _context = new CatalogContext(dbOptions);
        _orderRepository = new EfRepository<Order>(_context);
        _refundRepository = new EfRepository<OrderRefund>(_context);
        _catalogItemRepository = new EfRepository<CatalogItem>(_context);
        _paymentMethodRepository = new EfRepository<PaymentMethod>(_context);
        _gateway = Substitute.For<IPayPalGateway>();
    }

    private OrderPaymentService NewService()
    {
        return new OrderPaymentService(
            _gateway,
            _orderRepository,
            _refundRepository,
            _catalogItemRepository,
            _paymentMethodRepository,
            new PayPalOptions { Currency = "USD" },
            Substitute.For<IAppLogger<OrderPaymentService>>());
    }

    private static Address TestAddress()
    {
        return new Address("1 Main St", "Seattle", "WA", "US", "98101");
    }

    private static PayPalCardDetails TestCard()
    {
        return new PayPalCardDetails("Test Shopper", "4111111111111111", "2028-09", "123", null);
    }

    private async Task<CatalogItem> SeedCatalogItemAsync(decimal price)
    {
        var item = new CatalogItem(2, 2, "desc", "Test Product", price, "uri");
        await _catalogItemRepository.AddAsync(item);
        return item;
    }

    private async Task<Order> CreateOrderAsync(string buyerId, int catalogItemId, int quantity = 1)
    {
        var service = NewService();
        return await service.CreateOrderAsync(
            buyerId, new List<OrderLineItem> { new OrderLineItem(catalogItemId, quantity) }, TestAddress(), CancellationToken.None);
    }

    private void StubPayAsync(string orderId, string authorizationId, DateTimeOffset? expiration = null)
    {
        _gateway.CreateOrderAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<PayPalCardDetails>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCreateOrderResult(orderId, "APPROVED"));
        _gateway.AuthorizeOrderAsync(orderId, Arg.Any<PayPalCardDetails>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizeResult(orderId, "COMPLETED", authorizationId, "AUTHORIZED", expiration ?? DateTimeOffset.UtcNow.AddDays(3)));
    }

    [Fact]
    public async Task CreateOrderComputesTotalFromCatalogPrices()
    {
        var item = await SeedCatalogItemAsync(19.50m);

        var order = await CreateOrderAsync("buyer1", item.Id, 2);

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(39.00m, order.Total());
        Assert.Equal("USD", order.Currency);
    }

    [Fact]
    public async Task PayAuthorizesAndIsIdempotentInEffect()
    {
        var item = await SeedCatalogItemAsync(10.00m);
        var order = await CreateOrderAsync("buyer1", item.Id);
        StubPayAsync("ORDER-1", "AUTH-1");

        var service = NewService();
        var paid = await service.PayAsync("buyer1", order.Id, new OrderPaymentMethod(TestCard(), null), CancellationToken.None);

        Assert.Equal(OrderStatus.Authorized, paid.Status);
        Assert.Equal("AUTH-1", paid.AuthorizationId);
        Assert.Equal("ORDER-1", paid.PayPalOrderId);

        var paidAgain = await service.PayAsync("buyer1", order.Id, new OrderPaymentMethod(TestCard(), null), CancellationToken.None);

        Assert.Equal(OrderStatus.Authorized, paidAgain.Status);
        await _gateway.Received(1).CreateOrderAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<PayPalCardDetails>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayByAnotherBuyerThrowsNotFound()
    {
        var item = await SeedCatalogItemAsync(10.00m);
        var order = await CreateOrderAsync("buyer1", item.Id);
        StubPayAsync("ORDER-1", "AUTH-1");

        await Assert.ThrowsAsync<OrderNotFoundException>(() =>
            NewService().PayAsync("buyer2", order.Id, new OrderPaymentMethod(TestCard(), null), CancellationToken.None));
    }

    [Fact]
    public async Task FulfilCapturesAndRecordsReportedAmounts()
    {
        var item = await SeedCatalogItemAsync(10.00m);
        var order = await CreateOrderAsync("buyer1", item.Id);
        StubPayAsync("ORDER-1", "AUTH-1");
        var service = NewService();
        await service.PayAsync("buyer1", order.Id, new OrderPaymentMethod(TestCard(), null), CancellationToken.None);

        _gateway.CaptureAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCaptureResult("CAP-1", "COMPLETED", 10.00m, 0.50m, 9.50m, "USD"));

        var fulfilled = await service.FulfilAsync(order.Id, CancellationToken.None);

        Assert.Equal(OrderStatus.Fulfilled, fulfilled.Status);
        Assert.Equal("CAP-1", fulfilled.CaptureId);
        Assert.Equal(10.00m, fulfilled.CaptureGrossAmount);
        Assert.Equal(0.50m, fulfilled.CaptureFeeAmount);
        Assert.Equal(9.50m, fulfilled.CaptureNetAmount);

        var fulfilledAgain = await service.FulfilAsync(order.Id, CancellationToken.None);
        Assert.Equal(OrderStatus.Fulfilled, fulfilledAgain.Status);
        await _gateway.Received(1).CaptureAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FulfilRenewsStaleAuthorizationBeforeCapturing()
    {
        var item = await SeedCatalogItemAsync(10.00m);
        var order = await CreateOrderAsync("buyer1", item.Id);
        StubPayAsync("ORDER-1", "AUTH-1", expiration: DateTimeOffset.UtcNow.AddDays(-1));
        var service = NewService();
        await service.PayAsync("buyer1", order.Id, new OrderPaymentMethod(TestCard(), null), CancellationToken.None);

        _gateway.ReauthorizeAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizationActionResult("AUTH-1", "AUTHORIZED", DateTimeOffset.UtcNow.AddDays(3)));
        _gateway.CaptureAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCaptureResult("CAP-1", "COMPLETED", 10.00m, 0.50m, 9.50m, "USD"));

        await service.FulfilAsync(order.Id, CancellationToken.None);

        await _gateway.Received(1).ReauthorizeAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _gateway.Received(1).CaptureAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FulfilWithUnrenewableAuthorizationReportsOperatorActionableError()
    {
        var item = await SeedCatalogItemAsync(10.00m);
        var order = await CreateOrderAsync("buyer1", item.Id);
        StubPayAsync("ORDER-1", "AUTH-1", expiration: DateTimeOffset.UtcNow.AddDays(-1));
        var service = NewService();
        await service.PayAsync("buyer1", order.Id, new OrderPaymentMethod(TestCard(), null), CancellationToken.None);

        _gateway.ReauthorizeAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PayPalAuthorizationActionResult>(new PayPalApiException("too late", 422)));

        var ex = await Assert.ThrowsAsync<AuthorizationCannotBeRenewedException>(() =>
            service.FulfilAsync(order.Id, CancellationToken.None));

        Assert.Contains("pay again", ex.Message);
        await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelVoidsAuthorizationAndCancelsOrder()
    {
        var item = await SeedCatalogItemAsync(10.00m);
        var order = await CreateOrderAsync("buyer1", item.Id);
        StubPayAsync("ORDER-1", "AUTH-1");
        var service = NewService();
        await service.PayAsync("buyer1", order.Id, new OrderPaymentMethod(TestCard(), null), CancellationToken.None);

        _gateway.VoidAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizationActionResult("AUTH-1", "VOIDED", null));

        var cancelled = await service.CancelAsync(order.Id, CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        await _gateway.Received(1).VoidAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundRecordsAmountAndCapsAtRefundableBalance()
    {
        var item = await SeedCatalogItemAsync(10.00m);
        var order = await CreateOrderAsync("buyer1", item.Id);
        StubPayAsync("ORDER-1", "AUTH-1");
        var service = NewService();
        await service.PayAsync("buyer1", order.Id, new OrderPaymentMethod(TestCard(), null), CancellationToken.None);

        _gateway.CaptureAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCaptureResult("CAP-1", "COMPLETED", 10.00m, 0.50m, 9.50m, "USD"));
        await service.FulfilAsync(order.Id, CancellationToken.None);

        _gateway.RefundAsync("CAP-1", 4.00m, "USD", "key-1", Arg.Any<CancellationToken>())
            .Returns(new PayPalRefundResult("REF-1", "COMPLETED", 4.00m, "USD"));

        var (refunded, refund) = await service.RefundAsync(order.Id, 4.00m, "key-1", CancellationToken.None);

        Assert.Equal(OrderStatus.PartiallyRefunded, refunded.Status);
        Assert.Equal(4.00m, refunded.RefundedAmount);
        Assert.Equal("REF-1", refund.PayPalRefundId);

        // Refunding more than the remaining balance must be rejected.
        await Assert.ThrowsAsync<InvalidOrderStateException>(() =>
            service.RefundAsync(order.Id, 9.00m, "key-2", CancellationToken.None));

        // Repeating the same idempotency key must not refund twice.
        var (_, existing) = await service.RefundAsync(order.Id, 4.00m, "key-1", CancellationToken.None);
        Assert.Equal("REF-1", existing.PayPalRefundId);
        await _gateway.Received(1).RefundAsync("CAP-1", Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundFullAmountMarksOrderRefunded()
    {
        var item = await SeedCatalogItemAsync(10.00m);
        var order = await CreateOrderAsync("buyer1", item.Id);
        StubPayAsync("ORDER-1", "AUTH-1");
        var service = NewService();
        await service.PayAsync("buyer1", order.Id, new OrderPaymentMethod(TestCard(), null), CancellationToken.None);

        _gateway.CaptureAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCaptureResult("CAP-1", "COMPLETED", 10.00m, 0.50m, 9.50m, "USD"));
        await service.FulfilAsync(order.Id, CancellationToken.None);

        _gateway.RefundAsync("CAP-1", 10.00m, "USD", "key-1", Arg.Any<CancellationToken>())
            .Returns(new PayPalRefundResult("REF-1", "COMPLETED", 10.00m, "USD"));

        var (refunded, _) = await service.RefundAsync(order.Id, 10.00m, "key-1", CancellationToken.None);

        Assert.Equal(OrderStatus.Refunded, refunded.Status);
        Assert.Equal(10.00m, refunded.RefundedAmount);
    }

    [Fact]
    public async Task PaymentMethodsAreScopedToTheirOwner()
    {
        var service = NewService();
        _gateway.CreatePaymentTokenAsync(Arg.Any<PayPalCardDetails>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalPaymentTokenResult("TOKEN-1", "VISA", "1111", "2028-09"));

        var saved = await service.SavePaymentMethodAsync("buyer1", TestCard(), CancellationToken.None);
        Assert.Equal("TOKEN-1", saved.PayPalPaymentTokenId);

        await Assert.ThrowsAsync<PaymentMethodNotFoundException>(() =>
            service.DeletePaymentMethodAsync("buyer2", saved.Id, CancellationToken.None));

        var deleted = service.DeletePaymentMethodAsync("buyer1", saved.Id, CancellationToken.None);
        await deleted;

        var remaining = await service.GetPaymentMethodsAsync("buyer1", CancellationToken.None);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task SavedCardCanPayAnOrder()
    {
        var item = await SeedCatalogItemAsync(10.00m);
        var order = await CreateOrderAsync("buyer1", item.Id);

        var service = NewService();
        _gateway.CreatePaymentTokenAsync(Arg.Any<PayPalCardDetails>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalPaymentTokenResult("TOKEN-1", "VISA", "1111", "2028-09"));
        var saved = await service.SavePaymentMethodAsync("buyer1", TestCard(), CancellationToken.None);

        _gateway.CreateOrderAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<PayPalCardDetails>(), "TOKEN-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCreateOrderResult("ORDER-1", "APPROVED"));
        _gateway.AuthorizeOrderAsync("ORDER-1", Arg.Any<PayPalCardDetails>(), "TOKEN-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizeResult("ORDER-1", "COMPLETED", "AUTH-1", "AUTHORIZED", DateTimeOffset.UtcNow.AddDays(3)));

        var paid = await service.PayAsync("buyer1", order.Id, new OrderPaymentMethod(null, saved.Id), CancellationToken.None);

        Assert.Equal(OrderStatus.Authorized, paid.Status);
        await _gateway.Received(1).CreateOrderAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<PayPalCardDetails>(), "TOKEN-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconciliationLinesUpPayPalTransactionsWithOrders()
    {
        var item = await SeedCatalogItemAsync(10.00m);
        var order = await CreateOrderAsync("buyer1", item.Id);

        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow;

        _gateway.SearchTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<PayPalTransactionRecord>
            {
                new PayPalTransactionRecord("TXN-1", "some-paypal-ref", "T0001", "COMPLETED", from, 10.00m, 0.50m, "USD", "buyer@example.com", $"eshop-order-{order.Id}")
            });

        var rows = await NewService().GetReconciliationAsync(from, to, CancellationToken.None);

        var matched = Assert.Single(rows, r => r.Relation == "matched");
        Assert.Equal(order.Id, matched.OrderId);
        Assert.Equal("TXN-1", matched.PayPalTransactionId);
        Assert.Equal(9.50m, matched.NetAmount);
    }
}