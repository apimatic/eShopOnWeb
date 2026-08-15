using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderPaymentServiceTests
{
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _itemRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IReadRepository<SavedCard> _savedCardRead = Substitute.For<IReadRepository<SavedCard>>();
    private readonly IRepository<SavedCard> _savedCardRepo = Substitute.For<IRepository<SavedCard>>();
    private readonly IPayPalPaymentService _payPal = Substitute.For<IPayPalPaymentService>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderPaymentService> _logger = Substitute.For<IAppLogger<OrderPaymentService>>();

    private const string Buyer = "buyer@example.com";

    public OrderPaymentServiceTests()
    {
        _payPal.Currency.Returns("USD");
    }

    private OrderPaymentService CreateService() =>
        new(_orderRepo, _itemRepo, _savedCardRead, _savedCardRepo, _payPal, _uriComposer, _logger);

    private static Order AwaitingOrder(decimal unitPrice = 10m, int units = 1)
    {
        var address = new Address("1 St", "City", "ST", "US", "00000");
        var items = new List<OrderItem> { new(new CatalogItemOrdered(1, "Item", "pic.png"), unitPrice, units) };
        return new Order(Buyer, address, items);
    }

    private static Order CapturedOrder(decimal total)
    {
        var order = AwaitingOrder(total);
        var payment = new Payment(order.Id, total, "USD", "recon");
        payment.RecordAuthorization("PO", "AUTH", "CREATED", null, "VISA", "1111", null);
        order.SetAuthorized(payment);
        payment.RecordCapture("CAP", "COMPLETED", total, 1m, total - 1m);
        order.SetFulfilled();
        return order;
    }

    private void OrderRepoReturns(Order order) =>
        _orderRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(order);

    [Fact]
    public async Task Authorize_HoldsFundsAndRecordsState()
    {
        OrderRepoReturns(AwaitingOrder(25m));
        _payPal.AuthorizeAsync(Arg.Any<PayPalAuthorizeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizationResult
            {
                PayPalOrderId = "PO-1",
                OrderStatus = "COMPLETED",
                AuthorizationId = "AUTH-1",
                AuthorizationStatus = "CREATED",
                CardBrand = "VISA",
                CardLast4 = "1111"
            });

        var order = await CreateService().AuthorizeAsync(Buyer, 1,
            new PaymentInstrument { Card = new CardInput("4111111111111111", "2028-01", "123", "J D", null) });

        Assert.Equal(OrderStatus.Authorized, order.Status);
        Assert.Equal("AUTH-1", order.Payment!.AuthorizationId);
        Assert.Equal(25m, order.Payment.Amount);
    }

    [Fact]
    public async Task Authorize_IsIdempotent_WhenAlreadyAuthorized()
    {
        var order = AwaitingOrder(25m);
        var payment = new Payment(order.Id, 25m, "USD", "recon");
        payment.RecordAuthorization("PO", "AUTH", "CREATED", null, "VISA", "1111", null);
        order.SetAuthorized(payment);
        OrderRepoReturns(order);

        await CreateService().AuthorizeAsync(Buyer, 1,
            new PaymentInstrument { Card = new CardInput("4111111111111111", "2028-01", "123", "J D", null) });

        await _payPal.DidNotReceive().AuthorizeAsync(Arg.Any<PayPalAuthorizeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_WithForeignSavedCard_ThrowsNotFound()
    {
        OrderRepoReturns(AwaitingOrder(25m));
        _savedCardRead.FirstOrDefaultAsync(Arg.Any<ISpecification<SavedCard>>(), Arg.Any<CancellationToken>())
            .Returns((SavedCard?)null);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            CreateService().AuthorizeAsync(Buyer, 1, new PaymentInstrument { SavedCardId = 99 }));

        await _payPal.DidNotReceive().AuthorizeAsync(Arg.Any<PayPalAuthorizeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_CapturesAndRecordsFeeAndNet()
    {
        var order = AwaitingOrder(30m);
        var payment = new Payment(order.Id, 30m, "USD", "recon");
        payment.RecordAuthorization("PO", "AUTH", "CREATED", null, "VISA", "1111", null);
        order.SetAuthorized(payment);
        OrderRepoReturns(order);

        _payPal.GetAuthorizationAsync("AUTH", Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizationInfo("CREATED", null));
        _payPal.CaptureAsync("AUTH", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCaptureResult("CAP-1", "COMPLETED", 30m, 1.5m, 28.5m, "USD"));

        var result = await CreateService().FulfilAsync(1);

        Assert.Equal(OrderStatus.Fulfilled, result.Status);
        Assert.Equal("CAP-1", result.Payment!.CaptureId);
        Assert.Equal(1.5m, result.Payment.PayPalFee);
        Assert.Equal(28.5m, result.Payment.NetAmount);
    }

    [Fact]
    public async Task Refund_OverCap_Throws_AndDoesNotCallPayPal()
    {
        OrderRepoReturns(CapturedOrder(50m));

        await Assert.ThrowsAsync<PaymentException>(() =>
            CreateService().RefundAsync(Buyer, 1, 1000m, "key-1"));

        await _payPal.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_SameKey_IsIdempotent()
    {
        var order = CapturedOrder(50m);
        order.Payment!.AddRefund(new PaymentRefund("dup-key", "R-EXISTING", 5m, "USD", "COMPLETED"));
        OrderRepoReturns(order);

        var result = await CreateService().RefundAsync(Buyer, 1, 5m, "dup-key");

        Assert.Equal(5m, result.Payment!.TotalRefunded());
        await _payPal.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_Partial_CallsPayPalAndRecordsRefund()
    {
        OrderRepoReturns(CapturedOrder(50m));
        _payPal.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalRefundResult("R-1", "COMPLETED", 20m, "USD"));

        var result = await CreateService().RefundAsync(Buyer, 1, 20m, "key-1");

        Assert.Equal(OrderStatus.PartiallyRefunded, result.Status);
        Assert.Equal(20m, result.Payment!.TotalRefunded());
        Assert.Equal(30m, result.Payment.RemainingRefundable());
    }

    [Fact]
    public async Task Refund_UnknownOrder_ThrowsNotFound()
    {
        OrderRepoReturns(null!);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            CreateService().RefundAsync(Buyer, 123, 5m, "key-1"));
    }
}
