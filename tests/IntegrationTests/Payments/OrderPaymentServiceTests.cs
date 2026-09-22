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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

public class OrderPaymentServiceTests
{
    private readonly CatalogContext _ctx;
    private readonly FakePayPalPaymentGateway _gateway = new();
    private readonly OrderPaymentService _service;
    private int _itemId;

    public OrderPaymentServiceTests()
    {
        _ctx = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase("ops-" + Guid.NewGuid()).Options);
        _service = new OrderPaymentService(
            new EfRepository<Order>(_ctx),
            new EfRepository<OrderPayment>(_ctx),
            new EfRepository<OrderRefund>(_ctx),
            new EfRepository<CatalogItem>(_ctx),
            new EfRepository<SavedPaymentMethod>(_ctx),
            _gateway,
            new FakePaymentConfiguration(),
            new FakeUriComposer(),
            NullLogger<OrderPaymentService>.Instance);

        var item = new CatalogItem(1, 1, "desc", "Test Widget", 12.34m, "pic.png");
        _ctx.CatalogItems.Add(item);
        _ctx.SaveChanges();
        _itemId = item.Id;
    }

    private static CardDetails TestCard() =>
        new("4111111111111111", "2030-01", "123", "Test Buyer", null);

    [Fact]
    public async Task PlaceOrder_ComputesTotalFromCatalogPrices()
    {
        var orderId = await _service.PlaceOrderAsync("buyer@x.com",
            new List<PlaceOrderItem> { new(_itemId, 3) }, null, CancellationToken.None);

        var order = await _ctx.Orders.Include(o => o.OrderItems).FirstAsync(o => o.Id == orderId);
        Assert.Equal(3 * 12.34m, order.Total());
    }

    [Fact]
    public async Task Pay_Authorizes_AndIsIdempotentOnDoubleSubmit()
    {
        var orderId = await Place("buyer@x.com", 2);

        var first = await _service.PayAsync("buyer@x.com", orderId, TestCard(), null, CancellationToken.None);
        var second = await _service.PayAsync("buyer@x.com", orderId, TestCard(), null, CancellationToken.None);

        Assert.Equal(nameof(PaymentStatus.Authorized), first.Status);
        Assert.Equal(first.AuthorizationId, second.AuthorizationId);
        Assert.Equal(1, _gateway.AuthorizeCount); // second call did not authorize again
    }

    [Fact]
    public async Task Pay_RejectsAnotherShoppersOrder()
    {
        var orderId = await Place("owner@x.com", 1);
        await Assert.ThrowsAsync<PaymentFlowException>(() =>
            _service.PayAsync("intruder@x.com", orderId, TestCard(), null, CancellationToken.None));
    }

    [Fact]
    public async Task Fulfil_Captures_AndRecordsBreakdown()
    {
        var orderId = await Place("buyer@x.com", 1);
        await _service.PayAsync("buyer@x.com", orderId, TestCard(), null, CancellationToken.None);

        var view = await _service.FulfilAsync(orderId, CancellationToken.None);

        Assert.Equal(nameof(PaymentStatus.Fulfilled), view.Status);
        Assert.NotNull(view.CaptureId);
        Assert.Equal(100m, view.CapturedAmount);
        Assert.Equal(3.20m, view.PayPalFee);
        Assert.Equal(96.80m, view.NetAmount);
        Assert.Equal(1, _gateway.CaptureCount);
    }

    [Fact]
    public async Task Fulfil_IsIdempotent()
    {
        var orderId = await Place("buyer@x.com", 1);
        await _service.PayAsync("buyer@x.com", orderId, TestCard(), null, CancellationToken.None);
        await _service.FulfilAsync(orderId, CancellationToken.None);
        await _service.FulfilAsync(orderId, CancellationToken.None);
        Assert.Equal(1, _gateway.CaptureCount); // no second capture
    }

    [Fact]
    public async Task Cancel_VoidsBeforeFulfilment()
    {
        var orderId = await Place("buyer@x.com", 1);
        await _service.PayAsync("buyer@x.com", orderId, TestCard(), null, CancellationToken.None);

        var view = await _service.CancelAsync(orderId, CancellationToken.None);
        Assert.Equal(nameof(PaymentStatus.Cancelled), view.Status);
        Assert.Equal(1, _gateway.VoidCount);
    }

    [Fact]
    public async Task Refund_SameKeyDoesNotRefundTwice_DistinctKeysDo()
    {
        var orderId = await Place("buyer@x.com", 1);
        await _service.PayAsync("buyer@x.com", orderId, TestCard(), null, CancellationToken.None);
        await _service.FulfilAsync(orderId, CancellationToken.None); // captured 100

        var r1 = await _service.RefundAsync("buyer@x.com", orderId, 10m, "key-1", CancellationToken.None);
        var r1Again = await _service.RefundAsync("buyer@x.com", orderId, 10m, "key-1", CancellationToken.None);
        var r2 = await _service.RefundAsync("buyer@x.com", orderId, 15m, "key-2", CancellationToken.None);

        Assert.Equal(r1, r1Again);              // same key -> same refund id
        Assert.NotEqual(r1, r2);                // distinct keys -> distinct refunds
        Assert.Equal(2, _gateway.RefundCount);  // only two actual PayPal refunds
    }

    [Fact]
    public async Task Refund_CannotExceedCapturedAmount()
    {
        var orderId = await Place("buyer@x.com", 1);
        await _service.PayAsync("buyer@x.com", orderId, TestCard(), null, CancellationToken.None);
        await _service.FulfilAsync(orderId, CancellationToken.None); // captured 100

        await _service.RefundAsync("buyer@x.com", orderId, 60m, "k1", CancellationToken.None);
        var ex = await Assert.ThrowsAsync<PaymentFlowException>(() =>
            _service.RefundAsync("buyer@x.com", orderId, 60m, "k2", CancellationToken.None));
        Assert.Equal(PaymentFlowError.Validation, ex.Error);
    }

    [Fact]
    public async Task Pay_WithSavedCard_UsesVaultId()
    {
        var saved = new SavedPaymentMethod("buyer@x.com", "VAULT-XYZ", "CUST-1", "VISA", "1111", "2030-01", "Test Buyer");
        _ctx.SavedPaymentMethods.Add(saved);
        _ctx.SaveChanges();

        var orderId = await Place("buyer@x.com", 1);
        await _service.PayAsync("buyer@x.com", orderId, null, saved.Id, CancellationToken.None);

        Assert.Contains("VAULT-XYZ", _gateway.AuthorizeVaultIds);
    }

    private async Task<int> Place(string buyer, int qty) =>
        await _service.PlaceOrderAsync(buyer, new List<PlaceOrderItem> { new(_itemId, qty) }, null, CancellationToken.None);
}
