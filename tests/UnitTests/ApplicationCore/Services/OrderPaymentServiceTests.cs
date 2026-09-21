using System.Threading;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderPaymentServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<OrderPayment> _payments = Substitute.For<IRepository<OrderPayment>>();
    private readonly IRepository<SavedPaymentMethod> _savedCards = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IReadRepository<CatalogItem> _items = Substitute.For<IReadRepository<CatalogItem>>();
    private readonly IPayPalPaymentGateway _gateway = Substitute.For<IPayPalPaymentGateway>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderPaymentService> _logger = Substitute.For<IAppLogger<OrderPaymentService>>();

    private const string Buyer = "shopper@x.com";
    private readonly CardDetails _card = new("4111111111111111", "2030-12", "123", "Test", null);

    private OrderPaymentService CreateService()
    {
        _gateway.CurrencyCode.Returns("USD");
        return new OrderPaymentService(_orders, _payments, _savedCards, _items, _gateway, _uri, _logger);
    }

    private void GivenPayment(OrderPayment payment) =>
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>()).Returns(payment);

    private static OrderPayment AwaitingPayment() => new(1, Buyer, 20.00m, "USD");

    private static OrderPayment AuthorizedPayment()
    {
        var p = new OrderPayment(1, Buyer, 20.00m, "USD");
        p.MarkAuthorized("PPORD", "AUTH1", "CREATED", null, "VISA", "1111");
        return p;
    }

    [Fact]
    public async Task PayAuthorizesOnceForAwaitingOrder()
    {
        var svc = CreateService();
        GivenPayment(AwaitingPayment());
        _gateway.AuthorizeAsync(Arg.Any<AuthorizeCardRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizationResult("PPORD", "AUTH1", "CREATED", null, "VISA", "1111"));

        var result = await svc.PayAsync(Buyer, 1, new PayInstruction(_card, null), CancellationToken.None);

        Assert.Equal(OrderPaymentStatus.Authorized, result.Status);
        await _gateway.Received(1).AuthorizeAsync(Arg.Any<AuthorizeCardRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayIsIdempotentWhenAlreadyAuthorized()
    {
        var svc = CreateService();
        GivenPayment(AuthorizedPayment());

        await svc.PayAsync(Buyer, 1, new PayInstruction(_card, null), CancellationToken.None);

        // A double-click must not authorize again.
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<AuthorizeCardRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayRejectsAnotherShoppersOrder()
    {
        var svc = CreateService();
        GivenPayment(AwaitingPayment()); // owned by Buyer

        await Assert.ThrowsAsync<PaymentNotFoundException>(() =>
            svc.PayAsync("intruder@x.com", 1, new PayInstruction(_card, null), CancellationToken.None));
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<AuthorizeCardRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundIsIdempotentUnderSameKey()
    {
        var svc = CreateService();
        var p = AuthorizedPayment();
        p.MarkFulfilled("CAP1", "COMPLETED", 20.00m, 1.00m, 19.00m);
        p.AddRefund(new PaymentRefund("R1", 5.00m, "COMPLETED", "dup-key"));
        GivenPayment(p);

        var outcome = await svc.RefundAsync(Buyer, 1, 5.00m, "dup-key", null, CancellationToken.None);

        Assert.Equal("R1", outcome.Refund.PayPalRefundId);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundRejectsAmountBeyondCaptured()
    {
        var svc = CreateService();
        var p = AuthorizedPayment();
        p.MarkFulfilled("CAP1", "COMPLETED", 20.00m, 1.00m, 19.00m);
        p.AddRefund(new PaymentRefund("R1", 18.00m, "COMPLETED", "k1")); // remaining 2.00
        GivenPayment(p);

        await Assert.ThrowsAsync<PaymentValidationException>(() =>
            svc.RefundAsync(Buyer, 1, 5.00m, "k2", null, CancellationToken.None));
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FulfilRenewsStaleAuthorizationThenCaptures()
    {
        var svc = CreateService();
        var p = new OrderPayment(1, Buyer, 20.00m, "USD");
        // Authorized but already expired → must renew before capture.
        p.MarkAuthorized("PPORD", "AUTH1", "CREATED", System.DateTimeOffset.UtcNow.AddMinutes(-5), "VISA", "1111");
        GivenPayment(p);

        _gateway.ReauthorizeAsync("AUTH1", 20.00m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalReauthorizeResult("AUTH2", "CREATED", System.DateTimeOffset.UtcNow.AddDays(3)));
        _gateway.CaptureAsync("AUTH2", null, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCaptureResult("CAP1", "COMPLETED", 20.00m, 1.00m, 19.00m, "USD"));

        var result = await svc.FulfilAsync(1, CancellationToken.None);

        Assert.Equal(OrderPaymentStatus.Fulfilled, result.Status);
        Assert.Equal("CAP1", result.CaptureId);
        await _gateway.Received(1).ReauthorizeAsync("AUTH1", 20.00m, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _gateway.Received(1).CaptureAsync("AUTH2", null, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
