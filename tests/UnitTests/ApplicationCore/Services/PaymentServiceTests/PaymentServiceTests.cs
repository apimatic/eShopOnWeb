using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class PaymentServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _payments = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IReadRepository<SavedCard> _savedCards = Substitute.For<IReadRepository<SavedCard>>();
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<PaymentService> _logger = Substitute.For<IAppLogger<PaymentService>>();

    private PaymentService NewService() => new(_orders, _payments, _items, _savedCards, _gateway, _uri,
        Options.Create(new PayPalOptions { Currency = "USD" }), _logger);

    private void RepoReturns(Payment? p) =>
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<Payment>>(), Arg.Any<CancellationToken>())
            .Returns(p);

    private static Payment Authorized(string buyer = "owner@x", decimal amount = 29m)
    {
        var p = new Payment(1, buyer, amount, "USD");
        p.AssignInvoiceId("ESHOP-1-abc");
        p.MarkAuthorized("PPORDER", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "VISA ending 1111");
        return p;
    }

    private static Payment Captured(string buyer = "owner@x", decimal amount = 29m)
    {
        var p = Authorized(buyer, amount);
        p.MarkCaptured("CAP1", "COMPLETED", amount, 1.24m, amount - 1.24m);
        return p;
    }

    private static PayInput CardPay() => new(
        new CardInput("4111111111111111", "2030-01", "123", "N", "US", null, null, null, null, null), null);

    [Fact]
    public async Task Authorize_when_already_authorized_is_idempotent_and_does_not_call_the_gateway()
    {
        RepoReturns(Authorized());
        var view = await NewService().AuthorizeAsync(1, "owner@x", CardPay(), CancellationToken.None);

        Assert.Equal("Authorized", view.Status);
        Assert.Equal("AUTH1", view.AuthorizationId);
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<PayPalAuthorizeCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_on_another_shoppers_order_is_not_found_and_never_reaches_the_gateway()
    {
        RepoReturns(Authorized(buyer: "owner@x"));

        await Assert.ThrowsAsync<PaymentNotFoundException>(
            () => NewService().AuthorizeAsync(1, "attacker@x", CardPay(), CancellationToken.None));
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<PayPalAuthorizeCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_repeated_under_the_same_key_returns_the_same_refund_without_a_second_gateway_call()
    {
        var p = Captured();
        p.AddRefund(new PaymentRefund("key-1", "R-EXISTING", 10m, "USD", "COMPLETED"));
        RepoReturns(p);

        var view = await NewService().RefundAsync(1, "owner@x", 10m, "key-1", CancellationToken.None);

        Assert.Equal("R-EXISTING", view.RefundId);
        Assert.Equal(10m, view.RefundedTotal);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_exceeding_the_remaining_amount_is_rejected_before_the_gateway()
    {
        RepoReturns(Captured());

        await Assert.ThrowsAsync<PaymentStateException>(
            () => NewService().RefundAsync(1, "owner@x", 100m, "fresh-key", CancellationToken.None));
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancelling_a_captured_order_is_rejected_and_never_voids()
    {
        RepoReturns(Captured());

        await Assert.ThrowsAsync<PaymentStateException>(
            () => NewService().CancelAsync(1, CancellationToken.None));
        await _gateway.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfilling_an_authorized_order_captures_and_records_fee_and_net()
    {
        RepoReturns(Authorized());
        _gateway.CaptureAsync("AUTH1", 29m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCaptureResult("CAP1", "COMPLETED", 29m, 1.24m, 27.76m, "USD"));

        var view = await NewService().FulfilAsync(1, CancellationToken.None);

        Assert.Equal("Captured", view.Status);
        Assert.Equal(29m, view.CapturedAmount);
        Assert.Equal(1.24m, view.PayPalFee);
        Assert.Equal(27.76m, view.NetAmount);
    }

    [Fact]
    public async Task Order_not_found_yields_a_not_found_error()
    {
        RepoReturns(null);
        await Assert.ThrowsAsync<PaymentNotFoundException>(
            () => NewService().FulfilAsync(999, CancellationToken.None));
    }
}
