using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class PaymentServiceTests
{
    private const string Buyer = "demouser@microsoft.com";

    private readonly IOrderService _orderService = Substitute.For<IOrderService>();
    private readonly IRepository<OrderPayment> _paymentRepo = Substitute.For<IRepository<OrderPayment>>();
    private readonly IReadRepository<Order> _orderRepo = Substitute.For<IReadRepository<Order>>();
    private readonly IRepository<Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate.SavedPaymentMethod> _cardRepo =
        Substitute.For<IRepository<Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate.SavedPaymentMethod>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IPaymentConfiguration _config = Substitute.For<IPaymentConfiguration>();
    private readonly IAppLogger<PaymentService> _logger = Substitute.For<IAppLogger<PaymentService>>();

    private PaymentService CreateService()
    {
        _config.CurrencyCode.Returns("USD");
        return new PaymentService(_orderService, _paymentRepo, _orderRepo, _cardRepo, _gateway, _config, _logger);
    }

    private static OrderPayment NewPayment(string buyer = Buyer, decimal amount = 47.50m) =>
        new(orderId: 1, buyerId: buyer, amount: amount, currencyCode: "USD");

    private void ReturnsPayment(OrderPayment payment) =>
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<OrderPaymentByOrderIdSpec>(), Arg.Any<CancellationToken>()).Returns(payment);

    private static CardDetails TestCard() => new("4111111111111111", "2030-01", "123", "Demo User");

    [Fact]
    public async Task Authorize_WhenAlreadyAuthorized_IsIdempotent_AndDoesNotCallGateway()
    {
        var payment = NewPayment();
        payment.RecordPayPalOrder("PP1");
        payment.MarkAuthorized("AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(29));
        ReturnsPayment(payment);
        var service = CreateService();

        var result = await service.AuthorizeOrderAsync(Buyer, 1, TestCard(), null);

        Assert.Equal("Authorized", result.PaymentStatus);
        Assert.Equal("AUTH1", result.AuthorizationId);
        await _gateway.DidNotReceive().CreateAndAuthorizeAsync(Arg.Any<CreateAuthorizationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_PlacesHold_AndPersistsState()
    {
        var payment = NewPayment();
        ReturnsPayment(payment);
        var expiry = DateTimeOffset.UtcNow.AddDays(29);
        _gateway.CreateAndAuthorizeAsync(Arg.Any<CreateAuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorization("PP1", "AUTH1", "CREATED", expiry, false));
        var service = CreateService();

        var result = await service.AuthorizeOrderAsync(Buyer, 1, TestCard(), null);

        Assert.Equal("Authorized", result.PaymentStatus);
        Assert.Equal("AUTH1", result.AuthorizationId);
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        await _paymentRepo.Received().UpdateAsync(payment, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_WhenChallengeRequired_Throws()
    {
        var payment = NewPayment();
        ReturnsPayment(payment);
        _gateway.CreateAndAuthorizeAsync(Arg.Any<CreateAuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorization("PP1", "", "PAYER_ACTION_REQUIRED", null, RequiresBuyerAction: true));
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(
            () => service.AuthorizeOrderAsync(Buyer, 1, TestCard(), null));
        Assert.Equal("PAYER_ACTION_REQUIRED", ex.Issue);
    }

    [Fact]
    public async Task Authorize_ForeignOrder_Throws404()
    {
        ReturnsPayment(NewPayment(buyer: "someone-else@example.com"));
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<PaymentException>(
            () => service.AuthorizeOrderAsync(Buyer, 1, TestCard(), null));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Authorize_WithBothCardAndSavedCard_Throws400()
    {
        ReturnsPayment(NewPayment());
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<PaymentException>(
            () => service.AuthorizeOrderAsync(Buyer, 1, TestCard(), savedPaymentMethodId: 5));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Refund_SameIdempotencyKey_ReturnsExisting_WithoutCallingGateway()
    {
        var payment = NewPayment();
        payment.MarkAuthorized("AUTH1", "CREATED", null);
        payment.MarkCaptured("CAP1", "COMPLETED", 47.50m, 1.72m, 45.78m);
        payment.AddRefund("REF1", 10m, "COMPLETED", "key-a");
        ReturnsPayment(payment);
        var service = CreateService();

        var result = await service.RefundOrderAsync(Buyer, 1, 10m, "key-a");

        Assert.Equal("REF1", result.PayPalRefundId);
        Assert.Equal(10m, result.TotalRefunded);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_ExceedingRemaining_Throws422_WithoutCallingGateway()
    {
        var payment = NewPayment();
        payment.MarkAuthorized("AUTH1", "CREATED", null);
        payment.MarkCaptured("CAP1", "COMPLETED", 20m, 1m, 19m);
        ReturnsPayment(payment);
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<PaymentException>(
            () => service.RefundOrderAsync(Buyer, 1, 30m, "key-x"));
        Assert.Equal(422, ex.StatusCode);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_WhenNotCaptured_Throws409()
    {
        var payment = NewPayment();
        payment.MarkAuthorized("AUTH1", "CREATED", null); // authorized but not captured
        ReturnsPayment(payment);
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<PaymentException>(
            () => service.RefundOrderAsync(Buyer, 1, null, "key-x"));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Refund_SecondDistinctPartial_CallsGateway_AndTracksTotal()
    {
        var payment = NewPayment();
        payment.MarkAuthorized("AUTH1", "CREATED", null);
        payment.MarkCaptured("CAP1", "COMPLETED", 47.50m, 1.72m, 45.78m);
        payment.AddRefund("REF1", 10m, "COMPLETED", "key-a");
        ReturnsPayment(payment);
        _gateway.RefundAsync("CAP1", 5m, "USD", "key-b", Arg.Any<CancellationToken>())
            .Returns(new GatewayRefund("REF2", "COMPLETED", 5m, 15m));
        var service = CreateService();

        var result = await service.RefundOrderAsync(Buyer, 1, 5m, "key-b");

        Assert.Equal("REF2", result.PayPalRefundId);
        Assert.Equal(15m, result.TotalRefunded);
        Assert.Equal("PartiallyRefunded", result.PaymentStatus);
    }

    [Fact]
    public async Task Fulfil_WhenAlreadyCaptured_IsIdempotent()
    {
        var payment = NewPayment();
        payment.MarkAuthorized("AUTH1", "CREATED", null);
        payment.MarkCaptured("CAP1", "COMPLETED", 47.50m, 1.72m, 45.78m);
        ReturnsPayment(payment);
        var service = CreateService();

        var result = await service.FulfilOrderAsync(1);

        Assert.Equal("Captured", result.PaymentStatus);
        await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_VoidsAuthorization_AndReleasesFunds()
    {
        var payment = NewPayment();
        payment.MarkAuthorized("AUTH1", "CREATED", null);
        ReturnsPayment(payment);
        var service = CreateService();

        var result = await service.CancelOrderAsync(1);

        Assert.Equal("Voided", result.PaymentStatus);
        await _gateway.Received().VoidAsync("AUTH1", Arg.Any<CancellationToken>());
    }
}
