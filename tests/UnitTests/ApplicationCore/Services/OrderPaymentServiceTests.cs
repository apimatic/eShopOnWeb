using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderPaymentServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<OrderPayment> _payments = Substitute.For<IRepository<OrderPayment>>();
    private readonly IReadRepository<CatalogItem> _items = Substitute.For<IReadRepository<CatalogItem>>();
    private readonly IRepository<SavedCard> _cards = Substitute.For<IRepository<SavedCard>>();
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderPaymentService> _logger = Substitute.For<IAppLogger<OrderPaymentService>>();

    private OrderPaymentService CreateService()
    {
        var currency = Substitute.For<IPayPalCurrencyProvider>();
        currency.Currency.Returns("USD");
        _orders.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns((Order?)null);
        return new OrderPaymentService(_orders, _payments, _items, _cards, _gateway, _uri, _logger, currency);
    }

    private void GivenPayment(OrderPayment payment)
        => _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>())
            .Returns(payment);

    private static OrderPayment Authorized(string buyer = "me@x.com")
    {
        var p = new OrderPayment(1, buyer, 39.00m, "USD");
        p.MarkAuthorized("PPO", "AUTH1", "2099-01-01T00:00:00Z");
        return p;
    }

    private static OrderPayment Captured(string buyer = "me@x.com")
    {
        var p = Authorized(buyer);
        p.MarkCaptured("CAP1", 39.00m, 1.50m, 37.50m);
        return p;
    }

    [Fact]
    public async Task PayIsIdempotentWhenAlreadyAuthorized()
    {
        GivenPayment(Authorized());
        var svc = CreateService();

        var view = await svc.PayAsync("me@x.com", 1,
            new CardDetails("4111111111111111", "2030-01", "123", null, null), null, CancellationToken.None);

        Assert.Equal(PaymentStatus.Authorized, view.Status);
        await _gateway.DidNotReceiveWithAnyArgs().AuthorizeAsync(default!, default, default!, default!, default, default, default!, default);
    }

    [Fact]
    public async Task PayRejectsAnotherShoppersOrder()
    {
        GivenPayment(Authorized("owner@x.com"));
        var svc = CreateService();

        await Assert.ThrowsAsync<PaymentNotFoundException>(() =>
            svc.PayAsync("intruder@x.com", 1, null, 5, CancellationToken.None));
    }

    [Fact]
    public async Task RefundOverRemainingIsRejected()
    {
        GivenPayment(Captured());
        var svc = CreateService();

        await Assert.ThrowsAsync<PaymentValidationException>(() =>
            svc.RefundAsync("me@x.com", 1, 1000m, "k1", CancellationToken.None));
        await _gateway.DidNotReceiveWithAnyArgs().RefundAsync(default!, default, default!, default!, default!, default);
    }

    [Fact]
    public async Task RefundRepeatsUnderSameKeyReturnSameRefundWithoutCallingGateway()
    {
        var p = Captured();
        p.AddRefund(new PaymentRefund("k1", "REF1", 10.00m, "COMPLETED"));
        GivenPayment(p);
        var svc = CreateService();

        var result = await svc.RefundAsync("me@x.com", 1, 10.00m, "k1", CancellationToken.None);

        Assert.Equal("REF1", result.RefundId);
        await _gateway.DidNotReceiveWithAnyArgs().RefundAsync(default!, default, default!, default!, default!, default);
    }

    [Fact]
    public async Task CancelRejectedAfterCapture()
    {
        GivenPayment(Captured());
        var svc = CreateService();

        await Assert.ThrowsAsync<PaymentValidationException>(() => svc.CancelAsync(1, CancellationToken.None));
    }
}
