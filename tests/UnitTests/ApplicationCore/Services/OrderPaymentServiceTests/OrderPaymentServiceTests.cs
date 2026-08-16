using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class OrderPaymentServiceTests
{
    private const string Buyer = "buyer@test";

    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<OrderPayment> _payments = Substitute.For<IRepository<OrderPayment>>();
    private readonly IRepository<SavedCard> _cards = Substitute.For<IRepository<SavedCard>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderPaymentService> _logger = Substitute.For<IAppLogger<OrderPaymentService>>();

    private OrderPaymentService CreateService()
    {
        _gateway.CurrencyCode.Returns("USD");
        return new OrderPaymentService(_orders, _payments, _cards, _items, _gateway, _uri, _logger);
    }

    private OrderPayment PaymentInStatus(PaymentStatus status, decimal amount = 50m)
    {
        var payment = new OrderPayment(1, Buyer, amount, "USD");
        if (status >= PaymentStatus.Authorized)
        {
            payment.MarkAuthorized("PPO-1", "AUTH-1", "VISA ****1111", null);
        }
        if (status == PaymentStatus.Captured)
        {
            payment.MarkCaptured("CAP-1", amount, 1.5m, amount - 1.5m);
        }
        return payment;
    }

    private void RepoReturns(OrderPayment payment)
    {
        _payments.FirstOrDefaultAsync(Arg.Any<OrderPaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(payment);
    }

    [Fact]
    public async Task Authorize_WithVaultedCard_MarksAuthorized()
    {
        var service = CreateService();
        RepoReturns(PaymentInStatus(PaymentStatus.PendingAuthorization));
        _cards.FirstOrDefaultAsync(Arg.Any<SavedCardByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new SavedCard(Buyer, "VAULT-1", "VISA", "1111", "12", "2030", null));
        _gateway.AuthorizeWithVaultedCardAsync(Arg.Any<decimal>(), "VAULT-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PPO-9", "AUTH-9", "CREATED", "VISA", "1111", "12", "2030"));

        var result = await service.AuthorizeAsync(Buyer, 1, new PaymentInstrument(null, 7), CancellationToken.None);

        Assert.Equal(PaymentStatus.Authorized, result.Status);
        Assert.Equal("AUTH-9", result.AuthorizationId);
        await _gateway.Received(1).AuthorizeWithVaultedCardAsync(Arg.Any<decimal>(), "VAULT-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_WhenAlreadyAuthorized_IsIdempotent_AndDoesNotCallGateway()
    {
        var service = CreateService();
        RepoReturns(PaymentInStatus(PaymentStatus.Authorized));

        var card = new CardPaymentDetails("4111111111111111", "12", "2030", "123", "Test", null);
        var result = await service.AuthorizeAsync(Buyer, 1, new PaymentInstrument(card, null), CancellationToken.None);

        Assert.Equal(PaymentStatus.Authorized, result.Status);
        await _gateway.DidNotReceive().AuthorizeWithCardAsync(Arg.Any<decimal>(), Arg.Any<CardPaymentDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_ForAnotherBuyersOrder_IsNotFound()
    {
        var service = CreateService();
        RepoReturns(PaymentInStatus(PaymentStatus.PendingAuthorization));

        var card = new CardPaymentDetails("4111111111111111", "12", "2030", "123", "Test", null);
        await Assert.ThrowsAsync<PaymentResourceNotFoundException>(() =>
            service.AuthorizeAsync("someone-else", 1, new PaymentInstrument(card, null), CancellationToken.None));
    }

    [Fact]
    public async Task Fulfil_CapturesAndRecordsFeeAndNet()
    {
        var service = CreateService();
        RepoReturns(PaymentInStatus(PaymentStatus.Authorized));
        _gateway.CaptureAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP-9", "COMPLETED", 50m, 1.75m, 48.25m));

        var result = await service.FulfilAsync(1, CancellationToken.None);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        Assert.Equal("CAP-9", result.CaptureId);
        Assert.Equal(1.75m, result.PayPalFee);
        Assert.Equal(48.25m, result.NetAmount);
    }

    [Fact]
    public async Task Fulfil_WhenAuthorizationStale_ReauthorizesThenCaptures()
    {
        var service = CreateService();
        RepoReturns(PaymentInStatus(PaymentStatus.Authorized));

        // First capture (against the original, stale hold) fails; capture against the renewed hold succeeds.
        _gateway.CaptureAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AuthorizationExpiredException("authorization expired"));
        _gateway.ReauthorizeAsync("AUTH-1", Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("", "AUTH-2", "CREATED", null, null, null, null));
        _gateway.CaptureAsync("AUTH-2", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP-2", "COMPLETED", 50m, 1.5m, 48.5m));

        var result = await service.FulfilAsync(1, CancellationToken.None);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        Assert.Equal("AUTH-2", result.AuthorizationId);
        Assert.Equal("CAP-2", result.CaptureId);
        await _gateway.Received(1).ReauthorizeAsync("AUTH-1", Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_WhenReauthorizationNotAllowed_Propagates()
    {
        var service = CreateService();
        RepoReturns(PaymentInStatus(PaymentStatus.Authorized));
        _gateway.CaptureAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AuthorizationExpiredException("expired"));
        _gateway.ReauthorizeAsync("AUTH-1", Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ReauthorizationNotAllowedException("cannot reauthorize"));

        await Assert.ThrowsAsync<ReauthorizationNotAllowedException>(() => service.FulfilAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task Cancel_WhenAuthorized_VoidsHoldAndMarksCancelled()
    {
        var service = CreateService();
        RepoReturns(PaymentInStatus(PaymentStatus.Authorized));

        var result = await service.CancelAsync(1, CancellationToken.None);

        Assert.Equal(PaymentStatus.Cancelled, result.Status);
        await _gateway.Received(1).VoidAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_AfterCapture_IsRejected()
    {
        var service = CreateService();
        RepoReturns(PaymentInStatus(PaymentStatus.Captured));

        await Assert.ThrowsAsync<PaymentException>(() => service.CancelAsync(1, CancellationToken.None));
        await _gateway.DidNotReceive().VoidAuthorizationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_IsIdempotentOnKey_AndCallsGatewayOnce()
    {
        var service = CreateService();
        var payment = PaymentInStatus(PaymentStatus.Captured, 50m);
        RepoReturns(payment);
        _gateway.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult("R-1", "COMPLETED", 10m));

        var first = await service.RefundAsync(Buyer, 1, 10m, "key-1", CancellationToken.None);
        var second = await service.RefundAsync(Buyer, 1, 10m, "key-1", CancellationToken.None);

        Assert.Same(first, second);
        await _gateway.Received(1).RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_ExceedingCaptured_IsRejected()
    {
        var service = CreateService();
        RepoReturns(PaymentInStatus(PaymentStatus.Captured, 50m));

        await Assert.ThrowsAsync<PaymentException>(() =>
            service.RefundAsync(Buyer, 1, 999m, "key-1", CancellationToken.None));
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
