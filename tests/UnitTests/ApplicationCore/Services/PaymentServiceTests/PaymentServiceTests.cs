using System;
using System.Threading;
using System.Threading.Tasks;
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
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class PaymentServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _payments = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<SavedCard> _cards = Substitute.For<IRepository<SavedCard>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<PaymentService> _logger = Substitute.For<IAppLogger<PaymentService>>();

    private PaymentService Service() => new(_orders, _payments, _cards, _items, _gateway, _uri, _logger);

    public PaymentServiceTests() => _gateway.Currency.Returns("USD");

    private Payment FulfilledPayment(string buyer = "buyer-1")
    {
        var p = new Payment(1, buyer, "USD", 47.50m, "ESHOP-1-abc");
        p.MarkAuthorized("PP", "AUTH", DateTimeOffset.UtcNow.AddDays(29));
        p.MarkFulfilled("CAP", 47.50m, 1.72m, 45.78m);
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(p);
        return p;
    }

    [Fact]
    public async Task Refund_OverCapturedAmount_IsRejected()
    {
        FulfilledPayment();
        var svc = Service();

        await Assert.ThrowsAsync<PaymentValidationException>(() =>
            svc.RefundAsync("buyer-1", 1, amount: 100m, idempotencyKey: "k1", CancellationToken.None));

        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_IsIdempotentUnderSameKey()
    {
        var payment = FulfilledPayment();
        payment.AddRefund(new PaymentRefund("dup", "PP-REF", 10m, "COMPLETED"));
        var svc = Service();

        var outcome = await svc.RefundAsync("buyer-1", 1, amount: 10m, idempotencyKey: "dup", CancellationToken.None);

        Assert.Equal("PP-REF", outcome.PayPalRefundId);
        Assert.Equal(10m, outcome.TotalRefunded);
        // The gateway is NOT called again for a repeated key.
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_PartialHappyPath_CallsGatewayAndRecordsRefund()
    {
        var payment = FulfilledPayment();
        _gateway.RefundAsync("CAP", 10m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult("PP-REF-1", "COMPLETED", 10m));
        var svc = Service();

        var outcome = await svc.RefundAsync("buyer-1", 1, amount: 10m, idempotencyKey: "k1", CancellationToken.None);

        Assert.Equal("PP-REF-1", outcome.PayPalRefundId);
        Assert.Equal(10m, outcome.TotalRefunded);
        Assert.Equal(nameof(PaymentStatus.PartiallyRefunded), outcome.PaymentStatus);
        await _payments.Received().UpdateAsync(payment, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_OnAnotherShoppersOrder_IsNotFound()
    {
        FulfilledPayment(buyer: "someone-else");
        var svc = Service();

        await Assert.ThrowsAsync<PaymentResourceNotFoundException>(() =>
            svc.RefundAsync("buyer-1", 1, amount: 5m, idempotencyKey: "k1", CancellationToken.None));
    }

    [Fact]
    public async Task Pay_WhenAlreadyAuthorized_DoesNotAuthorizeAgain()
    {
        var p = new Payment(1, "buyer-1", "USD", 47.50m, "ESHOP-1-abc");
        p.MarkAuthorized("PP", "AUTH", DateTimeOffset.UtcNow.AddDays(29));
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(p);
        var svc = Service();

        var view = await svc.PayAsync("buyer-1", 1,
            new PayInstruction(new CardInput("4111111111111111", "2030-01", "123", "Demo", null), null), CancellationToken.None);

        Assert.Equal(nameof(PaymentStatus.Authorized), view.PaymentStatus);
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<AuthorizeInstruction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_WithNoPaymentSource_IsRejected()
    {
        var p = new Payment(1, "buyer-1", "USD", 47.50m, "ESHOP-1-abc");
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(p);
        var svc = Service();

        await Assert.ThrowsAsync<PaymentValidationException>(() =>
            svc.PayAsync("buyer-1", 1, new PayInstruction(null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Fulfil_CapturesAndRecordsFeeAndNet()
    {
        var p = new Payment(1, "buyer-1", "USD", 47.50m, "ESHOP-1-abc");
        p.MarkAuthorized("PP", "AUTH", DateTimeOffset.UtcNow.AddDays(29));
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(p);
        _gateway.GetAuthorizationAsync("AUTH", Arg.Any<CancellationToken>())
            .Returns(new AuthorizationSnapshot("CREATED", DateTimeOffset.UtcNow.AddDays(29)));
        _gateway.CaptureAsync("AUTH", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP-1", "COMPLETED", 47.50m, 1.72m, 45.78m, "USD"));
        var svc = Service();

        var view = await svc.FulfilAsync(1, CancellationToken.None);

        Assert.Equal(nameof(PaymentStatus.Fulfilled), view.PaymentStatus);
        Assert.Equal(45.78m, view.NetAmount);
        Assert.Equal(1.72m, view.PayPalFee);
        // A fresh (non-stale) authorization is captured directly, not reauthorized.
        await _gateway.DidNotReceive().ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_VoidsHeldAuthorization()
    {
        var p = new Payment(1, "buyer-1", "USD", 47.50m, "ESHOP-1-abc");
        p.MarkAuthorized("PP", "AUTH", DateTimeOffset.UtcNow.AddDays(29));
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(p);
        var svc = Service();

        var view = await svc.CancelAsync(1, CancellationToken.None);

        Assert.Equal(nameof(PaymentStatus.Cancelled), view.PaymentStatus);
        await _gateway.Received().VoidAsync("AUTH", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteSavedCard_OnAnotherShoppersCard_IsNotFound()
    {
        _cards.FirstOrDefaultAsync(Arg.Any<SavedCardByIdSpecification>(), Arg.Any<CancellationToken>()).Returns((SavedCard?)null);
        var svc = Service();

        await Assert.ThrowsAsync<PaymentResourceNotFoundException>(() =>
            svc.DeleteSavedCardAsync("buyer-1", 99, CancellationToken.None));

        await _gateway.DidNotReceive().DeleteVaultedCardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
