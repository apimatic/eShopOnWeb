using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class OrderPaymentServiceTests
{
    private const string Buyer = "shopper@example.com";

    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<OrderPayment> _payments = Substitute.For<IRepository<OrderPayment>>();
    private readonly IRepository<CatalogItem> _catalog = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<SavedPaymentMethod> _savedCards = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderPaymentService> _logger = Substitute.For<IAppLogger<OrderPaymentService>>();

    private OrderPaymentService CreateService()
    {
        _gateway.Currency.Returns("USD");
        return new OrderPaymentService(_orders, _payments, _catalog, _savedCards, _gateway, _uriComposer, _logger);
    }

    private static OrderPayment CapturedPayment(decimal amount = 29m)
    {
        var payment = new OrderPayment(1, Buyer, "USD", amount, "ESHOP-1-test");
        payment.RecordPayPalOrder("PPORDER1");
        payment.RecordAuthorization("AUTH1", "CREATED", "VISA ****1111");
        payment.RecordCapture("CAP1", "COMPLETED", amount, 1.24m, amount - 1.24m);
        return payment;
    }

    [Fact]
    public async Task Refund_RejectsAmountGreaterThanCaptured()
    {
        var payment = CapturedPayment(29m);
        _payments.FirstOrDefaultAsync(Arg.Any<OrderPaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidPaymentOperationException>(
            () => service.RefundAsync(Buyer, 1, 100m, "key-1", CancellationToken.None));

        await _gateway.DidNotReceive().RefundAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<GatewayMoney>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_IsIdempotentForSameKey()
    {
        var payment = CapturedPayment(29m);
        payment.AddRefund(new RefundRecord("dup-key", "REFUND-EXISTING", 5m, "COMPLETED"));
        _payments.FirstOrDefaultAsync(Arg.Any<OrderPaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);
        var service = CreateService();

        var result = await service.RefundAsync(Buyer, 1, 5m, "dup-key", CancellationToken.None);

        Assert.Equal("REFUND-EXISTING", result.PayPalRefundId);
        await _gateway.DidNotReceive().RefundAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<GatewayMoney>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_DistinctKeysProduceDistinctRefunds()
    {
        var payment = CapturedPayment(29m);
        _payments.FirstOrDefaultAsync(Arg.Any<OrderPaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);
        _gateway.RefundAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<GatewayMoney>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new RefundResult($"REFUND-{ci.ArgAt<string>(0)}", "COMPLETED", new GatewayMoney("USD", "5.00")));
        var service = CreateService();

        var first = await service.RefundAsync(Buyer, 1, 5m, "k1", CancellationToken.None);
        var second = await service.RefundAsync(Buyer, 1, 4m, "k2", CancellationToken.None);

        Assert.NotEqual(first.PayPalRefundId, second.PayPalRefundId);
        Assert.Equal(9m, payment.TotalRefunded);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
    }

    [Fact]
    public async Task Refund_OtherShoppersOrderIsNotFound()
    {
        var payment = CapturedPayment(29m);
        _payments.FirstOrDefaultAsync(Arg.Any<OrderPaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);
        var service = CreateService();

        await Assert.ThrowsAsync<OrderPaymentNotFoundException>(
            () => service.RefundAsync("someone-else@example.com", 1, 5m, "k1", CancellationToken.None));
    }

    [Fact]
    public async Task Authorize_AlreadyAuthorizedIsNoOp()
    {
        var payment = new OrderPayment(1, Buyer, "USD", 29m, "ESHOP-1-test");
        payment.RecordPayPalOrder("PPORDER1");
        payment.RecordAuthorization("AUTH1", "CREATED", "VISA ****1111");
        _payments.FirstOrDefaultAsync(Arg.Any<OrderPaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);
        var service = CreateService();

        var card = new CardDetails("4111111111111111", "2030-01", "123", "T", null);
        var result = await service.AuthorizeAsync(Buyer, 1, card, null, CancellationToken.None);

        Assert.Equal(PaymentStatus.Authorized, result.Status);
        await _gateway.DidNotReceive().CreateOrderAsync(
            Arg.Any<string>(), Arg.Any<GatewayMoney>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _gateway.DidNotReceive().AuthorizeOrderAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CardDetails?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_RenewsStaleAuthorizationThenCaptures()
    {
        var payment = new OrderPayment(1, Buyer, "USD", 29m, "ESHOP-1-test");
        payment.RecordPayPalOrder("PPORDER1");
        payment.RecordAuthorization("AUTH1", "CREATED", "VISA ****1111");
        _payments.FirstOrDefaultAsync(Arg.Any<OrderPaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);

        // First capture (against the original authorization) fails because it went stale.
        _gateway.CaptureAsync(Arg.Is<string>(r => r.EndsWith("-capture")), Arg.Any<string>(), Arg.Any<GatewayMoney>(), Arg.Any<CancellationToken>())
            .Returns<CaptureResult>(_ => throw new PayPalGatewayException("expired") { IsAuthorizationExpired = true });
        _gateway.ReauthorizeAsync(Arg.Any<string>(), "AUTH1", Arg.Any<GatewayMoney>(), Arg.Any<CancellationToken>())
            .Returns(new ReauthorizeResult("AUTH2", "CREATED"));
        _gateway.CaptureAsync(Arg.Is<string>(r => r.EndsWith("-capture-renewed")), "AUTH2", Arg.Any<GatewayMoney>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP2", "COMPLETED", new GatewayMoney("USD", "29.00"), new GatewayMoney("USD", "1.24"), new GatewayMoney("USD", "27.76")));

        var service = CreateService();
        var result = await service.FulfilAsync(1, CancellationToken.None);

        Assert.Equal(PaymentStatus.Fulfilled, result.Status);
        Assert.Equal("AUTH2", result.AuthorizationId);
        Assert.Equal("CAP2", result.CaptureId);
        Assert.Equal(27.76m, result.NetAmount);
        await _gateway.Received(1).ReauthorizeAsync(Arg.Any<string>(), "AUTH1", Arg.Any<GatewayMoney>(), Arg.Any<CancellationToken>());
    }
}
