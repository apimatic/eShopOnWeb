using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderPaymentServiceTests
{
    private const string Buyer = "shopper@example.com";

    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<OrderPayment> _paymentRepo = Substitute.For<IRepository<OrderPayment>>();
    private readonly IRepository<CatalogItem> _itemRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<SavedPaymentMethod> _savedRepo = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IPayPalPaymentGateway _gateway = Substitute.For<IPayPalPaymentGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();

    private OrderPaymentService CreateService()
    {
        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns(ci => (string)ci[0]);
        return new OrderPaymentService(_orderRepo, _paymentRepo, _itemRepo, _savedRepo, _gateway, _uriComposer,
            Options.Create(new PayPalSettings { Currency = "USD" }));
    }

    private Order OwnedOrder(string buyer = Buyer)
    {
        var items = new List<OrderItem>
        {
            new OrderItem(new CatalogItemOrdered(1, "Item", "pic.png"), 19.5m, 2)
        };
        return new Order(buyer, new Address("s", "c", "st", "co", "z"), items);
    }

    private void SetupOrderAndPayment(Order order, OrderPayment payment)
    {
        _orderRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<OrderPaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);
    }

    [Fact]
    public async Task Pay_WhenAlreadyAuthorized_IsIdempotent_AndDoesNotCallGatewayAgain()
    {
        var payment = new OrderPayment(1, Buyer, 39m, "USD");
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);
        SetupOrderAndPayment(OwnedOrder(), payment);
        var service = CreateService();

        var result = await service.PayAsync(1, new PaymentInstrument { Card = SampleCard() }, Buyer);

        Assert.Equal(PaymentStatus.Authorized, result.Status);
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<PayPalAuthorizationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_WhenChallengeRequired_ThrowsActionRequired_AndMarksActionRequired()
    {
        var payment = new OrderPayment(1, Buyer, 39m, "USD");
        SetupOrderAndPayment(OwnedOrder(), payment);
        _gateway.AuthorizeAsync(Arg.Any<PayPalAuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizationResult { PayPalOrderId = "PPORDER", RequiresAction = true });
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentActionRequiredException>(() =>
            service.PayAsync(1, new PaymentInstrument { Card = SampleCard() }, Buyer));

        Assert.Equal(PaymentStatus.ActionRequired, payment.Status);
    }

    [Fact]
    public async Task Pay_ForAnotherBuyersOrder_ThrowsNotFound()
    {
        var payment = new OrderPayment(1, "someone.else@example.com", 39m, "USD");
        SetupOrderAndPayment(OwnedOrder("someone.else@example.com"), payment);
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentEntityNotFoundException>(() =>
            service.PayAsync(1, new PaymentInstrument { Card = SampleCard() }, Buyer));
    }

    [Fact]
    public async Task Refund_WithSameIdempotencyKey_DoesNotRefundTwice()
    {
        var payment = new OrderPayment(1, Buyer, 39m, "USD");
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);
        payment.MarkCaptured("CAP1", "COMPLETED", 39m, 1.5m, 37.5m);
        payment.AddRefund("dup-key", 10m, "REF1", "COMPLETED");
        SetupOrderAndPayment(OwnedOrder(), payment);
        var service = CreateService();

        var result = await service.RefundAsync(1, 10m, "dup-key", Buyer);

        Assert.Equal("REF1", result.PayPalRefundId);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_ExceedingCaptured_Throws()
    {
        var payment = new OrderPayment(1, Buyer, 39m, "USD");
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);
        payment.MarkCaptured("CAP1", "COMPLETED", 39m, 1.5m, 37.5m);
        SetupOrderAndPayment(OwnedOrder(), payment);
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidPaymentOperationException>(() =>
            service.RefundAsync(1, 100m, "key-1", Buyer));
    }

    [Fact]
    public async Task Cancel_WhenAuthorized_VoidsTheHold()
    {
        var payment = new OrderPayment(1, Buyer, 39m, "USD");
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);
        SetupOrderAndPayment(OwnedOrder(), payment);
        var service = CreateService();

        var result = await service.CancelAsync(1);

        Assert.Equal(PaymentStatus.Voided, result.Status);
        await _gateway.Received(1).VoidAsync("AUTH1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_WhenAuthorized_CapturesAndRecordsProceeds()
    {
        var payment = new OrderPayment(1, Buyer, 39m, "USD");
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);
        SetupOrderAndPayment(OwnedOrder(), payment);
        _gateway.CaptureAsync("AUTH1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCaptureResult { CaptureId = "CAP1", Status = "COMPLETED", GrossAmount = 39m, PayPalFee = 1.5m, NetAmount = 37.5m, CurrencyCode = "USD" });
        var service = CreateService();

        var result = await service.FulfilAsync(1);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        Assert.Equal(39m, result.CapturedGrossAmount);
        Assert.Equal(1.5m, result.PayPalFeeAmount);
        Assert.Equal(37.5m, result.NetAmount);
    }

    private static PayPalCardDetails SampleCard() => new()
    {
        Number = "4111111111111111",
        Expiry = "2030-12",
        SecurityCode = "123"
    };
}
