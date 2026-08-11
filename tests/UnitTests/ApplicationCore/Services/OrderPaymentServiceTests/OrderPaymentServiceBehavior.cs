using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class OrderPaymentServiceBehavior
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _payments = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<SavedCard> _cards = Substitute.For<IRepository<SavedCard>>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IPayPalPaymentGateway _gateway = Substitute.For<IPayPalPaymentGateway>();
    private readonly IAppLogger<OrderPaymentService> _logger = Substitute.For<IAppLogger<OrderPaymentService>>();

    private OrderPaymentService CreateService() =>
        new(_orders, _payments, _items, _cards, _uri, _gateway,
            Options.Create(new PayPalSettings { Currency = "USD" }), _logger);

    private void ReturnsPayment(Payment payment) =>
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpec>(), Arg.Any<CancellationToken>()).Returns(payment);

    private static Payment CapturedPayment(string buyerId = "buyer@test", decimal gross = 20m)
    {
        var payment = new Payment(1, buyerId, gross, "USD");
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);
        payment.MarkCaptured("CAP1", "COMPLETED", gross, 1m, gross - 1m);
        return payment;
    }

    [Fact]
    public async Task PayIsIdempotentWhenAlreadyAuthorized()
    {
        var payment = new Payment(1, "buyer@test", 20m, "USD");
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);
        ReturnsPayment(payment);
        var service = CreateService();

        await service.PayAsync("buyer@test", 1, new PaymentInstruction { Card = new CardDetails() });

        // A second authorization must never be attempted.
        await _gateway.DidNotReceive().AuthorizeWithCardAsync(
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayOnAnotherShoppersOrderIsNotFound()
    {
        ReturnsPayment(new Payment(1, "someone-else@test", 20m, "USD"));
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentNotFoundException>(() =>
            service.PayAsync("buyer@test", 1, new PaymentInstruction { Card = new CardDetails() }));
    }

    [Fact]
    public async Task RefundUnderSameKeyDoesNotRefundTwice()
    {
        var payment = CapturedPayment();
        payment.AddRefund(new PaymentRefund("dup", "R1", 5m, "USD", "COMPLETED"));
        ReturnsPayment(payment);
        var service = CreateService();

        var outcome = await service.RefundAsync("buyer@test", 1, 5m, "dup");

        Assert.Equal("R1", outcome.PayPalRefundId);
        await _gateway.DidNotReceive().RefundAsync(
            Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundBeyondCapturedAmountIsRejected()
    {
        var payment = CapturedPayment(gross: 20m);
        payment.AddRefund(new PaymentRefund("k1", "R1", 15m, "USD", "COMPLETED")); // 5 remaining
        ReturnsPayment(payment);
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidPaymentOperationException>(() =>
            service.RefundAsync("buyer@test", 1, 10m, "k2"));

        await _gateway.DidNotReceive().RefundAsync(
            Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundDistinctPartialsAreAllowed()
    {
        var payment = CapturedPayment(gross: 20m);
        ReturnsPayment(payment);
        _gateway.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new RefundResult { RefundId = "R-" + ci.ArgAt<string>(3), Status = "COMPLETED", Amount = ci.ArgAt<decimal?>(1) ?? 0m, Currency = "USD" });
        var service = CreateService();

        var first = await service.RefundAsync("buyer@test", 1, 5m, "k1");
        var second = await service.RefundAsync("buyer@test", 1, 7m, "k2");

        Assert.Equal("R-k1", first.PayPalRefundId);
        Assert.Equal("R-k2", second.PayPalRefundId);
        Assert.Equal(12m, payment.TotalRefunded);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
    }

    [Fact]
    public async Task FulfilOnUnpaidOrderIsRejected()
    {
        ReturnsPayment(new Payment(1, "buyer@test", 20m, "USD")); // AwaitingPayment
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidPaymentOperationException>(() => service.FulfilAsync(1));

        await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
