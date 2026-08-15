using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class PaymentServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _payments = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<PaymentMethod> _methods = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IRepository<CatalogItem> _catalog = Substitute.For<IRepository<CatalogItem>>();
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<PaymentService> _logger = Substitute.For<IAppLogger<PaymentService>>();

    private PaymentService CreateService() =>
        new(_orders, _payments, _methods, _catalog, _gateway, _uriComposer, new PayPalOptions { Currency = "USD" }, _logger);

    private void ReturnPayment(Payment payment) =>
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);

    private static Payment AuthorizedPayment(string buyer = "buyer@test", decimal amount = 47.50m)
    {
        var p = new Payment(1, buyer, amount, "USD");
        p.MarkAuthorized("po-1", "auth-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), null);
        return p;
    }

    private static Payment CapturedPayment(string buyer = "buyer@test", decimal amount = 47.50m)
    {
        var p = AuthorizedPayment(buyer, amount);
        p.MarkCaptured("cap-1", "COMPLETED", amount, 1.72m, amount - 1.72m);
        return p;
    }

    [Fact]
    public async Task Authorize_IsIdempotent_WhenAlreadyAuthorized()
    {
        ReturnPayment(AuthorizedPayment());
        var service = CreateService();

        var result = await service.AuthorizeAsync(1, "buyer@test",
            new CardDetails("N", "4111111111111111", "2027-01", "123"), null);

        Assert.Equal(PaymentStatus.Authorized, result.Status);
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<AuthorizeInstruction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_OnDecline_KeepsAwaitingPayment_AndRotatesKey()
    {
        var payment = new Payment(1, "buyer@test", 47.50m, "USD");
        var originalKey = payment.AuthorizeRequestId;
        ReturnPayment(payment);
        _gateway.AuthorizeAsync(Arg.Any<AuthorizeInstruction>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new PayPalApiException("Card declined"));
        var service = CreateService();

        await Assert.ThrowsAsync<PayPalApiException>(() =>
            service.AuthorizeAsync(1, "buyer@test", new CardDetails("N", "4111111111111111", "2027-01", "123"), null));

        Assert.Equal(PaymentStatus.AwaitingPayment, payment.Status);
        Assert.NotEqual(originalKey, payment.AuthorizeRequestId);
    }

    [Fact]
    public async Task Authorize_AnotherShoppersOrder_IsNotFound()
    {
        ReturnPayment(AuthorizedPayment(buyer: "owner@test"));
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentNotFoundException>(() =>
            service.AuthorizeAsync(1, "intruder@test", new CardDetails("N", "4111111111111111", "2027-01", "123"), null));
    }

    [Fact]
    public async Task Authorize_WithUnknownSavedCard_IsNotFound()
    {
        ReturnPayment(new Payment(1, "buyer@test", 47.50m, "USD"));
        _methods.FirstOrDefaultAsync(Arg.Any<PaymentMethodByIdForBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns((PaymentMethod?)null);
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentNotFoundException>(() =>
            service.AuthorizeAsync(1, "buyer@test", card: null, paymentMethodId: 99));
    }

    [Fact]
    public async Task Fulfil_Captures_AndStoresFeeAndNet()
    {
        var payment = AuthorizedPayment();
        ReturnPayment(payment);
        _gateway.CaptureAsync("auth-1", "USD", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("cap-9", "COMPLETED", 47.50m, 1.72m, 45.78m, "USD"));
        var service = CreateService();

        var result = await service.FulfilAsync(1);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        Assert.Equal(47.50m, result.CapturedAmount);
        Assert.Equal(1.72m, result.PayPalFee);
        Assert.Equal(45.78m, result.NetAmount);
    }

    [Fact]
    public async Task Fulfil_RenewsStaleAuthorization_BeforeCapturing()
    {
        var payment = new Payment(1, "buyer@test", 47.50m, "USD");
        payment.MarkAuthorized("po-1", "auth-old", "CREATED", DateTimeOffset.UtcNow.AddMinutes(-5), null); // stale
        ReturnPayment(payment);
        _gateway.ReauthorizeAsync("auth-old", 47.50m, "USD", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("po-1", "auth-new", "CREATED", DateTimeOffset.UtcNow.AddDays(29)));
        _gateway.CaptureAsync("auth-new", "USD", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("cap-9", "COMPLETED", 47.50m, 1.72m, 45.78m, "USD"));
        var service = CreateService();

        var result = await service.FulfilAsync(1);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        await _gateway.Received(1).ReauthorizeAsync("auth-old", 47.50m, "USD", Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_IsIdempotent_WhenAlreadyCaptured()
    {
        ReturnPayment(CapturedPayment());
        var service = CreateService();

        var result = await service.FulfilAsync(1);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_Voids_AnAuthorizedPayment()
    {
        var payment = AuthorizedPayment();
        ReturnPayment(payment);
        _gateway.VoidAsync("auth-1", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new VoidResult("auth-1", "VOIDED"));
        var service = CreateService();

        var result = await service.CancelAsync(1);

        Assert.Equal(PaymentStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Cancel_AfterCapture_IsRejected()
    {
        ReturnPayment(CapturedPayment());
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentStateException>(() => service.CancelAsync(1));
    }

    [Fact]
    public async Task Refund_OverRemaining_IsRejected_AndNotSentToPayPal()
    {
        ReturnPayment(CapturedPayment(amount: 47.50m));
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentValidationException>(() =>
            service.RefundAsync(1, "buyer@test", 1000m, "key-x"));

        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_SameIdempotencyKey_DoesNotRefundTwice()
    {
        var payment = CapturedPayment();
        ReturnPayment(payment);
        _gateway.RefundAsync("cap-1", 10m, "USD", "key-a", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("refund-a", "COMPLETED", 10m, "USD"));
        var service = CreateService();

        await service.RefundAsync(1, "buyer@test", 10m, "key-a");
        await service.RefundAsync(1, "buyer@test", 10m, "key-a"); // repeat under same key

        Assert.Equal(10m, payment.TotalRefunded);
        await _gateway.Received(1).RefundAsync("cap-1", 10m, "USD", "key-a", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_TwoDistinctKeys_AreTwoLegitimatePartials()
    {
        var payment = CapturedPayment();
        ReturnPayment(payment);
        _gateway.RefundAsync("cap-1", 10m, "USD", "key-a", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("refund-a", "COMPLETED", 10m, "USD"));
        _gateway.RefundAsync("cap-1", 5m, "USD", "key-b", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("refund-b", "COMPLETED", 5m, "USD"));
        var service = CreateService();

        await service.RefundAsync(1, "buyer@test", 10m, "key-a");
        await service.RefundAsync(1, "buyer@test", 5m, "key-b");

        Assert.Equal(15m, payment.TotalRefunded);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
    }
}
