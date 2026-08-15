using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class PaymentServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _payments = Substitute.For<IRepository<Payment>>();
    private readonly IReadRepository<CatalogItem> _catalog = Substitute.For<IReadRepository<CatalogItem>>();
    private readonly IReadRepository<PaymentMethod> _cards = Substitute.For<IReadRepository<PaymentMethod>>();
    private readonly IPayPalGateway _paypal = Substitute.For<IPayPalGateway>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<PaymentService> _logger = Substitute.For<IAppLogger<PaymentService>>();

    private PaymentService Service() =>
        new(_orders, _payments, _catalog, _cards, _paypal, _uri, new PayPalSettings { Currency = "USD" }, _logger);

    private static Payment AuthorizedPayment()
    {
        var payment = new Payment(1, "12345", 3.69m, "USD");
        payment.SetAuthorization("PP-ORDER", "AUTH-1", "CREATED", null, "Card ****1111");
        return payment;
    }

    [Fact]
    public async Task AuthorizeIsIdempotentOnceAHoldExists()
    {
        var order = new OrderBuilder().WithDefaultValues(); // BuyerId "12345"
        order.MarkAuthorized(); // consistent with an existing hold
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(order);
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(AuthorizedPayment());

        var instruction = new AuthorizeInstruction(
            new CardDetails("4111111111111111", "2030-01", "123", "Buyer", null, null, null, null, null, "US"), null);

        var view = await Service().AuthorizeAsync("12345", 1, instruction);

        Assert.Equal("PaymentAuthorized", view.Status);
        // The hold already exists, so PayPal is never asked to authorize a second time.
        await _paypal.DidNotReceive().AuthorizeWithCardAsync(
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthorizeAnotherShoppersOrderIsNotFound()
    {
        var order = new OrderBuilder().WithDefaultValues(); // BuyerId "12345"
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(order);

        var instruction = new AuthorizeInstruction(
            new CardDetails("4111111111111111", "2030-01", "123", "Buyer", null, null, null, null, null, "US"), null);

        var ex = await Assert.ThrowsAsync<PaymentApiException>(
            () => Service().AuthorizeAsync("someone-else", 1, instruction));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task PayingWithBothCardAndSavedCardIsRejected()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(order);
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns((Payment?)null);

        var instruction = new AuthorizeInstruction(
            new CardDetails("4111111111111111", "2030-01", "123", "Buyer", null, null, null, null, null, "US"), 7);

        var ex = await Assert.ThrowsAsync<PaymentApiException>(
            () => Service().AuthorizeAsync("12345", 1, instruction));
        Assert.Equal(400, ex.StatusCode);
    }
}
