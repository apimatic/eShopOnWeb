using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class PaymentServiceTests
{
    private const string Buyer = "buyer@example.com";

    private readonly IRepository<Payment> _payments = Substitute.For<IRepository<Payment>>();
    private readonly IReadRepository<Order> _orders = Substitute.For<IReadRepository<Order>>();
    private readonly IReadRepository<SavedCard> _savedCards = Substitute.For<IReadRepository<SavedCard>>();
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();
    private readonly IAppLogger<PaymentService> _logger = Substitute.For<IAppLogger<PaymentService>>();

    private PaymentService CreateService()
    {
        var options = Options.Create(new PayPalSettings { Currency = "USD" });
        _payments.AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Payment>());
        return new PaymentService(_payments, _orders, _savedCards, _gateway, options, _logger);
    }

    private static Order OrderFor(string buyer, decimal unitPrice, int qty)
    {
        var itemOrdered = new CatalogItemOrdered(1, "Widget", "pic.png");
        var order = new Order(buyer, new Address("s", "c", "st", "co", "z"),
            new List<OrderItem> { new(itemOrdered, unitPrice, qty) });
        return order;
    }

    [Fact]
    public async Task AuthorizeWithCard_CreatesAuthorizedPayment()
    {
        var service = CreateService();
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(OrderFor(Buyer, 50m, 2));
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns((Payment?)null);
        _gateway.AuthorizeWithCardAsync(Arg.Any<Money>(), Arg.Any<CardDetails>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PPO1", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));

        var card = new CardDetails("4111111111111111", "12", "2030", "123", "Jane", null);
        var payment = await service.AuthorizeAsync(1, Buyer, null, card);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(100m, payment.Amount); // 50 * 2, from catalog price
        await _gateway.Received(1).AuthorizeWithCardAsync(Arg.Is<Money>(m => m.Value == 100m && m.CurrencyCode == "USD"),
            Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_IsIdempotent_WhenPaymentAlreadyExists()
    {
        var service = CreateService();
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(OrderFor(Buyer, 50m, 2));
        var existing = new Payment(1, Buyer, "USD", 100m, "PPO1", "AUTH1", DateTimeOffset.UtcNow.AddDays(3));
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var card = new CardDetails("4111111111111111", "12", "2030", "123", "Jane", null);
        var payment = await service.AuthorizeAsync(1, Buyer, null, card);

        Assert.Same(existing, payment);
        await _gateway.DidNotReceive().AuthorizeWithCardAsync(Arg.Any<Money>(), Arg.Any<CardDetails>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_Rejects_WhenBothCardAndSavedCardGiven()
    {
        var service = CreateService();
        var card = new CardDetails("4111111111111111", "12", "2030", "123", "Jane", null);
        await Assert.ThrowsAsync<PaymentValidationException>(() =>
            service.AuthorizeAsync(1, Buyer, savedCardId: 5, card: card));
    }

    [Fact]
    public async Task Authorize_Rejects_WhenNeitherCardNorSavedCardGiven()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<PaymentValidationException>(() =>
            service.AuthorizeAsync(1, Buyer, savedCardId: null, card: null));
    }

    [Fact]
    public async Task Authorize_HidesOthersOrders()
    {
        var service = CreateService();
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(OrderFor("someone-else@example.com", 50m, 2));

        var card = new CardDetails("4111111111111111", "12", "2030", "123", "Jane", null);
        await Assert.ThrowsAsync<OrderNotFoundException>(() =>
            service.AuthorizeAsync(1, Buyer, null, card));
    }

    [Fact]
    public async Task Refund_IsIdempotent_UnderSameKey()
    {
        var service = CreateService();
        var payment = new Payment(1, Buyer, "USD", 100m, "PPO1", "AUTH1", DateTimeOffset.UtcNow.AddDays(3));
        payment.MarkCaptured("CAP1", 100m, 3m, 97m);
        payment.AddRefund("REF1", "key-1", 40m, "COMPLETED");
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(payment);

        var refund = await service.RefundAsync(1, Buyer, 40m, "key-1");

        Assert.Equal("REF1", refund.PayPalRefundId);
        await _gateway.DidNotReceive().RefundCaptureAsync(Arg.Any<string>(), Arg.Any<Money?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_Rejects_WhenExceedingRemaining()
    {
        var service = CreateService();
        var payment = new Payment(1, Buyer, "USD", 100m, "PPO1", "AUTH1", DateTimeOffset.UtcNow.AddDays(3));
        payment.MarkCaptured("CAP1", 100m, 3m, 97m);
        payment.AddRefund("REF1", "key-1", 80m, "COMPLETED");
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(payment);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RefundAsync(1, Buyer, 40m, "key-2"));
    }

    [Fact]
    public async Task Fulfil_CapturesAndRecordsFigures()
    {
        var service = CreateService();
        var payment = new Payment(1, Buyer, "USD", 100m, "PPO1", "AUTH1", DateTimeOffset.UtcNow.AddDays(3));
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(payment);
        _gateway.CaptureAuthorizationAsync("AUTH1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP1", "COMPLETED", 100m, 3.2m, 96.8m, "USD"));

        var result = await service.FulfilAsync(1);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        Assert.Equal("CAP1", result.CaptureId);
        Assert.Equal(96.8m, result.NetAmount);
    }
}
