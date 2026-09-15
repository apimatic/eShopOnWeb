using System;
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
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

/// <summary>
/// Exercises the payment state machine against a fake gateway (the network seam), so authorize/fulfil/
/// cancel/refund, idempotency, over-refund guarding and ownership are verified without touching PayPal.
/// </summary>
public class PaymentServiceTests
{
    private const string Buyer = "shopper@example.com";
    private const string Other = "someone-else@example.com";

    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _payments = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<SavedCard> _savedCards = Substitute.For<IRepository<SavedCard>>();
    private readonly IReadRepository<CatalogItem> _catalog = Substitute.For<IReadRepository<CatalogItem>>();
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<PaymentService> _logger = Substitute.For<IAppLogger<PaymentService>>();

    private PaymentService CreateService() =>
        new(_orders, _payments, _savedCards, _catalog, _gateway, _uri, _logger);

    private static Order MakeOrder(string buyer = Buyer)
    {
        var items = new List<OrderItem> { new(new CatalogItemOrdered(1, "n", "p"), 10m, 1) };
        return new Order(buyer, new Address("s", "c", "st", "co", "z"), items);
    }

    private void ArrangeOrderAndPayment(int orderId, Payment payment, Order? order = null)
    {
        _orders.GetByIdAsync(orderId, Arg.Any<CancellationToken>()).Returns(order ?? MakeOrder());
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(payment);
    }

    [Fact]
    public async Task Authorize_PlacesHold_AndPersists()
    {
        var payment = new Payment(1, Buyer, 10m, "USD");
        ArrangeOrderAndPayment(1, payment);
        _gateway.AuthorizeAsync(10m, Arg.Any<CardPaymentInstrument>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PPO", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));

        var card = new CardDetails("4111111111111111", "2027-12", "123", null, null, null, null, null, null, null);
        var result = await CreateService().AuthorizeOrderAsync(1, Buyer, new PaymentInstruction(card, null), CancellationToken.None);

        Assert.Equal(PaymentStatus.Authorized, result.Status);
        Assert.Equal("AUTH1", result.AuthorizationId);
        await _payments.Received(1).UpdateAsync(payment, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_IsIdempotent_WhenAlreadyAuthorized()
    {
        var payment = new Payment(1, Buyer, 10m, "USD");
        payment.SetAuthorized("PPO", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        ArrangeOrderAndPayment(1, payment);

        var card = new CardDetails("4111111111111111", "2027-12", "123", null, null, null, null, null, null, null);
        await CreateService().AuthorizeOrderAsync(1, Buyer, new PaymentInstruction(card, null), CancellationToken.None);

        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<decimal>(), Arg.Any<CardPaymentInstrument>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_OtherShoppersOrder_IsNotFound()
    {
        var payment = new Payment(1, Buyer, 10m, "USD");
        ArrangeOrderAndPayment(1, payment, MakeOrder(Buyer));

        var card = new CardDetails("4111111111111111", "2027-12", "123", null, null, null, null, null, null, null);
        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            CreateService().AuthorizeOrderAsync(1, Other, new PaymentInstruction(card, null), CancellationToken.None));
    }

    [Fact]
    public async Task Authorize_WithSavedCardOfAnotherShopper_IsNotFound()
    {
        var payment = new Payment(1, Buyer, 10m, "USD");
        ArrangeOrderAndPayment(1, payment);
        var foreignCard = new SavedCard(Other, "vault-x", "VISA", "1111", "2027-12");
        _savedCards.GetByIdAsync(9, Arg.Any<CancellationToken>()).Returns(foreignCard);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            CreateService().AuthorizeOrderAsync(1, Buyer, new PaymentInstruction(null, 9), CancellationToken.None));
    }

    [Fact]
    public async Task Fulfil_Captures_AndRecordsFeeAndNet()
    {
        var payment = new Payment(1, Buyer, 10m, "USD");
        payment.SetAuthorized("PPO", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        ArrangeOrderAndPayment(1, payment);
        _gateway.CaptureAsync("AUTH1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP1", "COMPLETED", 10m, 0.59m, 9.41m));

        var result = await CreateService().FulfilOrderAsync(1, CancellationToken.None);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        Assert.Equal("CAP1", result.CaptureId);
        Assert.Equal(0.59m, result.PayPalFee);
        Assert.Equal(9.41m, result.NetAmount);
    }

    [Fact]
    public async Task Fulfil_RenewsStaleAuthorization_BeforeCapturing()
    {
        var payment = new Payment(1, Buyer, 10m, "USD");
        payment.SetAuthorized("PPO", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddMinutes(-5)); // already expired
        ArrangeOrderAndPayment(1, payment);
        _gateway.ReauthorizeAsync("AUTH1", 10m, Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("", "AUTH2", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
        _gateway.CaptureAsync("AUTH2", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP1", "COMPLETED", 10m, 0.59m, 9.41m));

        var result = await CreateService().FulfilOrderAsync(1, CancellationToken.None);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        await _gateway.Received(1).ReauthorizeAsync("AUTH1", 10m, Arg.Any<CancellationToken>());
        await _gateway.Received(1).CaptureAsync("AUTH2", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_VoidsHold_AndMarksCancelled()
    {
        var payment = new Payment(1, Buyer, 10m, "USD");
        payment.SetAuthorized("PPO", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        ArrangeOrderAndPayment(1, payment);

        var result = await CreateService().CancelOrderAsync(1, CancellationToken.None);

        Assert.Equal(PaymentStatus.Cancelled, result.Status);
        await _gateway.Received(1).VoidAsync("AUTH1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_AfterCapture_IsRejected()
    {
        var payment = new Payment(1, Buyer, 10m, "USD");
        payment.SetAuthorized("PPO", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        payment.SetCaptured("CAP1", "COMPLETED", 10m, 0.59m, 9.41m);
        ArrangeOrderAndPayment(1, payment);

        await Assert.ThrowsAsync<InvalidPaymentOperationException>(() =>
            CreateService().CancelOrderAsync(1, CancellationToken.None));
        await _gateway.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_RepeatedIdempotencyKey_DoesNotRefundTwice()
    {
        var payment = new Payment(1, Buyer, 10m, "USD");
        payment.SetAuthorized("PPO", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        payment.SetCaptured("CAP1", "COMPLETED", 10m, 0.59m, 9.41m);
        ArrangeOrderAndPayment(1, payment);
        _gateway.RefundAsync("CAP1", 4m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult("REF1", "COMPLETED", 4m));

        var service = CreateService();
        var first = await service.RefundOrderAsync(1, 4m, "key-1", CancellationToken.None);
        var second = await service.RefundOrderAsync(1, 4m, "key-1", CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        await _gateway.Received(1).RefundAsync("CAP1", 4m, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_TwoDistinctKeys_AreBothLegitimate()
    {
        var payment = new Payment(1, Buyer, 10m, "USD");
        payment.SetAuthorized("PPO", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        payment.SetCaptured("CAP1", "COMPLETED", 10m, 0.59m, 9.41m);
        ArrangeOrderAndPayment(1, payment);
        _gateway.RefundAsync("CAP1", Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult("REF1", "COMPLETED", 4m), new RefundResult("REF2", "COMPLETED", 3m));

        var service = CreateService();
        await service.RefundOrderAsync(1, 4m, "key-1", CancellationToken.None);
        await service.RefundOrderAsync(1, 3m, "key-2", CancellationToken.None);

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(7m, payment.TotalRefunded);
        await _gateway.Received(2).RefundAsync("CAP1", Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_BeyondCapturedAmount_IsRejected_AndNeverCallsGateway()
    {
        var payment = new Payment(1, Buyer, 10m, "USD");
        payment.SetAuthorized("PPO", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        payment.SetCaptured("CAP1", "COMPLETED", 10m, 0.59m, 9.41m);
        ArrangeOrderAndPayment(1, payment);

        await Assert.ThrowsAsync<PaymentValidationException>(() =>
            CreateService().RefundOrderAsync(1, 100m, "key-1", CancellationToken.None));
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteSavedCard_OfAnotherShopper_IsNotFound_AndNotDeletedFromVault()
    {
        var foreignCard = new SavedCard(Other, "vault-x", "VISA", "1111", "2027-12");
        _savedCards.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(foreignCard);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            CreateService().DeleteSavedCardAsync(Buyer, 5, CancellationToken.None));
        await _gateway.DidNotReceive().DeleteVaultedCardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteSavedCard_RemovesFromVaultAndStore()
    {
        var card = new SavedCard(Buyer, "vault-x", "VISA", "1111", "2027-12");
        _savedCards.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(card);

        await CreateService().DeleteSavedCardAsync(Buyer, 5, CancellationToken.None);

        await _gateway.Received(1).DeleteVaultedCardAsync("vault-x", Arg.Any<CancellationToken>());
        await _savedCards.Received(1).DeleteAsync(card, Arg.Any<CancellationToken>());
    }
}
