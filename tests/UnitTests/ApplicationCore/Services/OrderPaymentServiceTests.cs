using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderPaymentServiceTests
{
    private const string BuyerId = "buyer@example.com";
    private const int OrderId = 1;

    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _paymentRepo = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<CatalogItem> _itemRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<SavedPaymentMethod> _methodRepo = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IPayPalPaymentService _payPal = Substitute.For<IPayPalPaymentService>();

    private OrderPaymentService CreateService()
    {
        _payPal.Currency.Returns("USD");
        return new OrderPaymentService(_orderRepo, _paymentRepo, _itemRepo, _methodRepo, _uriComposer, _payPal);
    }

    private void PaymentInRepo(Payment payment) =>
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<Payment>>(), Arg.Any<CancellationToken>())
            .Returns(payment);

    private static Payment PendingPayment(decimal amount = 47.50m) => new(OrderId, BuyerId, "USD", amount);

    private static Payment AuthorizedPayment(DateTimeOffset? expiry = null)
    {
        var p = PendingPayment();
        p.BeginAuthorization();
        p.SetAuthorized("PP-ORDER", "AUTH-1", "CREATED", expiry ?? DateTimeOffset.UtcNow.AddDays(3), null);
        return p;
    }

    private static Payment CapturedPayment(decimal amount = 50m)
    {
        var p = AuthorizedPayment();
        p.BeginCapture();
        p.SetCaptured("CAP-1", "COMPLETED", amount, 1.5m, amount - 1.5m);
        return p;
    }

    [Fact]
    public async Task Authorize_WrongBuyer_ThrowsNotFound_AndDoesNotCallPayPal()
    {
        PaymentInRepo(PendingPayment());
        var service = CreateService();
        var card = new CardPaymentDetails("4111111111111111", 12, 2030, "123");

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            service.AuthorizeAsync("someone-else@example.com", OrderId, card, null));

        await _payPal.DidNotReceiveWithAnyArgs().AuthorizeWithCardAsync(default, default!, default!, default!);
    }

    [Fact]
    public async Task Authorize_AlreadyAuthorized_IsIdempotent()
    {
        PaymentInRepo(AuthorizedPayment());
        var service = CreateService();
        var card = new CardPaymentDetails("4111111111111111", 12, 2030, "123");

        var result = await service.AuthorizeAsync(BuyerId, OrderId, card, null);

        Assert.Equal(PaymentStatus.Authorized, result.Status);
        await _payPal.DidNotReceiveWithAnyArgs().AuthorizeWithCardAsync(default, default!, default!, default!);
    }

    [Fact]
    public async Task Authorize_WithCard_PlacesHold()
    {
        PaymentInRepo(PendingPayment());
        _payPal.AuthorizeWithCardAsync(Arg.Any<decimal>(), "USD", Arg.Any<CardPaymentDetails>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PP-ORDER", "AUTH-9", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
        var service = CreateService();
        var card = new CardPaymentDetails("4111111111111111", 12, 2030, "123");

        var result = await service.AuthorizeAsync(BuyerId, OrderId, card, null);

        Assert.Equal(PaymentStatus.Authorized, result.Status);
        Assert.Equal("AUTH-9", result.AuthorizationId);
        await _payPal.Received(1).AuthorizeWithCardAsync(47.50m, "USD", card, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_WithSavedCard_UsesVaultAndCustomer()
    {
        PaymentInRepo(PendingPayment());
        var saved = new SavedPaymentMethod(BuyerId, "VAULT-1", "CUST-1", "VISA", "1111", "2031-11", "Holder");
        _methodRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<SavedPaymentMethod>>(), Arg.Any<CancellationToken>())
            .Returns(saved);
        _payPal.AuthorizeWithVaultedCardAsync(Arg.Any<decimal>(), "USD", "VAULT-1", "CUST-1",
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PP-ORDER", "AUTH-V", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
        var service = CreateService();

        var result = await service.AuthorizeAsync(BuyerId, OrderId, null, savedPaymentMethodId: 1);

        Assert.Equal("AUTH-V", result.AuthorizationId);
        Assert.Equal(saved.Id, result.SavedPaymentMethodId);
        await _payPal.Received(1).AuthorizeWithVaultedCardAsync(47.50m, "USD", "VAULT-1", "CUST-1",
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_StaleAuthorization_RenewsThenCapturesWithNewAuthId()
    {
        PaymentInRepo(AuthorizedPayment(expiry: DateTimeOffset.UtcNow.AddMinutes(-5)));
        _payPal.ReauthorizeAsync("AUTH-1", 47.50m, "USD", Arg.Any<CancellationToken>())
            .Returns(new ReauthorizationResult("AUTH-2", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
        _payPal.CaptureAsync("AUTH-2", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP-2", "COMPLETED", 47.50m, 1.72m, 45.78m));
        var service = CreateService();

        var result = await service.FulfilAsync(OrderId);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        Assert.Equal(45.78m, result.NetAmount);
        await _payPal.Received(1).ReauthorizeAsync("AUTH-1", 47.50m, "USD", Arg.Any<CancellationToken>());
        await _payPal.Received(1).CaptureAsync("AUTH-2", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_AlreadyCaptured_IsIdempotent()
    {
        PaymentInRepo(CapturedPayment());
        var service = CreateService();

        await service.FulfilAsync(OrderId);

        await _payPal.DidNotReceiveWithAnyArgs().CaptureAsync(default!, default!);
    }

    [Fact]
    public async Task Refund_SameKeyReplay_DoesNotRefundTwice()
    {
        var payment = CapturedPayment(50m);
        PaymentInRepo(payment);
        _payPal.RefundAsync("CAP-1", 10m, "USD", "key-1", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("REF-1", "COMPLETED", 10m, 10m));
        var service = CreateService();

        var first = await service.RefundAsync(BuyerId, OrderId, 10m, "key-1");
        var second = await service.RefundAsync(BuyerId, OrderId, 10m, "key-1");

        Assert.Same(first, second);
        await _payPal.Received(1).RefundAsync("CAP-1", 10m, "USD", "key-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_BeyondCaptured_Throws_AndDoesNotCallPayPal()
    {
        PaymentInRepo(CapturedPayment(20m));
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentConflictException>(() =>
            service.RefundAsync(BuyerId, OrderId, 25m, "key-x"));

        await _payPal.DidNotReceiveWithAnyArgs().RefundAsync(default!, default, default!, default!);
    }

    [Fact]
    public async Task Cancel_VoidsAndMarksCancelled()
    {
        PaymentInRepo(AuthorizedPayment());
        var service = CreateService();

        var result = await service.CancelAsync(OrderId);

        Assert.Equal(PaymentStatus.Cancelled, result.Status);
        await _payPal.Received(1).VoidAsync("AUTH-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_AfterCapture_Throws()
    {
        PaymentInRepo(CapturedPayment());
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentConflictException>(() => service.CancelAsync(OrderId));
        await _payPal.DidNotReceiveWithAnyArgs().VoidAsync(default!);
    }
}
