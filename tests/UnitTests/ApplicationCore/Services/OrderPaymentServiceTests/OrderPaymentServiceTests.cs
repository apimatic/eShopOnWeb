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
    private const string Buyer = "shopper@example.com";
    private const string Other = "someone-else@example.com";

    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _payments = Substitute.For<IRepository<Payment>>();
    private readonly IReadRepository<CatalogItem> _items = Substitute.For<IReadRepository<CatalogItem>>();
    private readonly IRepository<SavedPaymentMethod> _cards = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IPaymentSettings _settings = Substitute.For<IPaymentSettings>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderPaymentService> _logger = Substitute.For<IAppLogger<OrderPaymentService>>();

    private static readonly CardDetails Card = new("4111111111111111", "12", "2030", "123");

    public OrderPaymentServiceTests() => _settings.CurrencyCode.Returns("USD");

    private OrderPaymentService Sut() =>
        new(_orders, _payments, _items, _cards, _gateway, _settings, _uri, _logger);

    private static Order BuildOrder(string buyerId)
    {
        var items = new System.Collections.Generic.List<OrderItem>
        {
            new(new CatalogItemOrdered(1, "Widget", "pic.png"), 10m, 1)
        };
        return new Order(buyerId, new Address("s", "c", "st", "US", "0"), items);
    }

    private void OrderExists(Order order) =>
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);

    private void PaymentExists(Payment payment) =>
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);

    [Fact]
    public async Task Authorize_Throws_WhenOrderNotOwnedByCaller()
    {
        OrderExists(BuildOrder(Other));
        await Assert.ThrowsAsync<PaymentNotFoundException>(() =>
            Sut().AuthorizeAsync(Buyer, 1, Card, null));
        await _gateway.DidNotReceive().AuthorizeWithCardAsync(default, default!, default!, default!, default);
    }

    [Fact]
    public async Task Authorize_IsIdempotent_WhenAlreadyAuthorized()
    {
        OrderExists(BuildOrder(Buyer));
        var payment = new Payment(1, Buyer, 10m, "USD");
        payment.MarkAuthorized("O1", "A1", "CREATED", null, null);
        PaymentExists(payment);

        var result = await Sut().AuthorizeAsync(Buyer, 1, Card, null);

        Assert.Equal("A1", result.AuthorizationId);
        await _gateway.DidNotReceive().AuthorizeWithCardAsync(
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_Throws_WhenBothCardAndSavedCardProvided()
    {
        await Assert.ThrowsAsync<PaymentException>(() => Sut().AuthorizeAsync(Buyer, 1, Card, 5));
    }

    [Fact]
    public async Task Authorize_Throws_WhenNeitherCardNorSavedCardProvided()
    {
        await Assert.ThrowsAsync<PaymentException>(() => Sut().AuthorizeAsync(Buyer, 1, null, null));
    }

    [Fact]
    public async Task Refund_Throws_WhenAmountExceedsRefundable()
    {
        OrderExists(BuildOrder(Buyer));
        var payment = new Payment(1, Buyer, 20m, "USD");
        payment.MarkAuthorized("O1", "A1", "CREATED", null, null);
        payment.MarkCaptured("C1", "COMPLETED", 20m, 1m, 19m);
        PaymentExists(payment);

        await Assert.ThrowsAsync<PaymentException>(() =>
            Sut().RefundAsync(Buyer, 1, 50m, "key-1"));
        await _gateway.DidNotReceive().RefundCaptureAsync(
            Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_IsIdempotent_UnderSameKey()
    {
        OrderExists(BuildOrder(Buyer));
        var payment = new Payment(1, Buyer, 20m, "USD");
        payment.MarkAuthorized("O1", "A1", "CREATED", null, null);
        payment.MarkCaptured("C1", "COMPLETED", 20m, 1m, 19m);
        payment.AddRefund("R1", 5m, "COMPLETED", "key-1");
        PaymentExists(payment);

        var result = await Sut().RefundAsync(Buyer, 1, 5m, "key-1");

        Assert.Equal("R1", result.PayPalRefundId);
        await _gateway.DidNotReceive().RefundCaptureAsync(
            Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_Throws_WhenNotAuthorized()
    {
        var payment = new Payment(1, Buyer, 20m, "USD"); // AwaitingPayment
        PaymentExists(payment);
        await Assert.ThrowsAsync<PaymentException>(() => Sut().CancelAsync(1));
    }

    [Fact]
    public async Task Fulfil_RenewsStaleAuthorization_ThenCaptures()
    {
        var payment = new Payment(1, Buyer, 20m, "USD");
        payment.MarkAuthorized("O1", "A1", "CREATED", null, null);
        PaymentExists(payment);

        _gateway.CaptureAuthorizationAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<GatewayCapture>(new AuthorizationExpiredException("stale")),
                _ => Task.FromResult(new GatewayCapture("C2", "COMPLETED", 20m, 1m, 19m, "USD")));
        _gateway.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorization("", "A2", "CREATED", null));

        var result = await Sut().FulfilAsync(1);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        Assert.Equal("A2", result.AuthorizationId); // renewed hold
        Assert.Equal("C2", result.CaptureId);
        await _gateway.Received(1).ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_Throws_NotRenewable_WhenReauthorizationFails()
    {
        var payment = new Payment(1, Buyer, 20m, "USD");
        payment.MarkAuthorized("O1", "A1", "CREATED", null, null);
        PaymentExists(payment);

        _gateway.CaptureAuthorizationAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new AuthorizationExpiredException("stale"));
        _gateway.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new PaymentGatewayException("beyond re-authorization window"));

        await Assert.ThrowsAsync<AuthorizationNotRenewableException>(() => Sut().FulfilAsync(1));
    }
}
