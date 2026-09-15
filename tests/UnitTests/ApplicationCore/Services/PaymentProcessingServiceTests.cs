using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class PaymentProcessingServiceTests
{
    private readonly IRepository<Order> _orderRepository = Substitute.For<IRepository<Order>>();
    private readonly IReadRepository<CatalogItem> _itemRepository = Substitute.For<IReadRepository<CatalogItem>>();
    private readonly IRepository<Buyer> _buyerRepository = Substitute.For<IRepository<Buyer>>();
    private readonly IPayPalPaymentGateway _gateway = Substitute.For<IPayPalPaymentGateway>();
    private readonly IPaymentCurrencyProvider _currency = Substitute.For<IPaymentCurrencyProvider>();
    private readonly IAppLogger<PaymentProcessingService> _logger = Substitute.For<IAppLogger<PaymentProcessingService>>();

    private const string Buyer = "shopper@example.com";

    public PaymentProcessingServiceTests()
    {
        _currency.Currency.Returns("USD");
    }

    private PaymentProcessingService CreateService() =>
        new(_orderRepository, _itemRepository, _buyerRepository, _gateway, _currency, _logger);

    private static Order NewOrder(decimal total = 29m)
    {
        var items = new List<OrderItem>
        {
            new OrderItem(new CatalogItemOrdered(1, "Item", "pic.png"), total, 1),
        };
        return new Order(Buyer, new Address("s", "c", "st", "US", "00000"), items);
    }

    private static Order AuthorizedOrder(decimal total = 29m, DateTimeOffset? expiresAt = null)
    {
        var order = NewOrder(total);
        var payment = new Payment("ESHOP-run-0", total, "USD", "PPO", "auth-run-0");
        payment.SetAuthorization("AUTH-1", "CREATED", expiresAt ?? DateTimeOffset.UtcNow.AddDays(29));
        order.AttachPayment(payment);
        return order;
    }

    private static Order CapturedOrder(decimal total = 29m)
    {
        var order = AuthorizedOrder(total);
        order.Payment!.SetCapture("CAP-1", "COMPLETED", total, 1.24m, total - 1.24m);
        order.MarkPaid();
        return order;
    }

    private void OrderRepoReturns(Order? order) =>
        _orderRepository.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(order);

    [Fact]
    public async Task Authorize_is_idempotent_when_a_hold_already_exists()
    {
        OrderRepoReturns(AuthorizedOrder());
        var service = CreateService();

        var result = await service.AuthorizeOrderAsync(Buyer, 1, new PaymentInstrument(null, 5));

        Assert.Equal(OrderStatus.PaymentAuthorized, result.Status);
        await _gateway.DidNotReceiveWithAnyArgs().AuthorizeOrderAsync(default!, default);
    }

    [Fact]
    public async Task Authorize_places_a_hold_and_records_paypal_state()
    {
        OrderRepoReturns(NewOrder());
        _gateway.AuthorizeOrderAsync(Arg.Any<AuthorizeGatewayRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorization("PPO-9", "COMPLETED", "AUTH-9", "CREATED", DateTimeOffset.UtcNow.AddDays(29), false));
        var card = new CardDetails("4111111111111111", "2030-01", "123", "N", null, null, null, null, null, "US");
        var service = CreateService();

        var result = await service.AuthorizeOrderAsync(Buyer, 1, new PaymentInstrument(card, null));

        Assert.Equal(OrderStatus.PaymentAuthorized, result.Status);
        Assert.Equal("AUTH-9", result.Payment!.AuthorizationId);
        await _orderRepository.Received().UpdateAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_surfaces_a_browser_challenge_instead_of_building_a_round_trip()
    {
        OrderRepoReturns(NewOrder());
        _gateway.AuthorizeOrderAsync(Arg.Any<AuthorizeGatewayRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorization("PPO", "PAYER_ACTION_REQUIRED", null, null, null, true));
        var card = new CardDetails("4111111111111111", "2030-01", "123", "N", null, null, null, null, null, "US");
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentChallengeRequiredException>(() =>
            service.AuthorizeOrderAsync(Buyer, 1, new PaymentInstrument(card, null)));
    }

    [Fact]
    public async Task Authorize_rejects_supplying_both_a_card_and_a_saved_card()
    {
        OrderRepoReturns(NewOrder());
        var card = new CardDetails("4111111111111111", "2030-01", "123", "N", null, null, null, null, null, "US");
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentException>(() =>
            service.AuthorizeOrderAsync(Buyer, 1, new PaymentInstrument(card, 5)));
    }

    [Fact]
    public async Task Fulfil_renews_a_stale_hold_then_captures()
    {
        var order = AuthorizedOrder();
        OrderRepoReturns(order);

        _gateway.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorization(string.Empty, "UNKNOWN", "AUTH-2", "CREATED", DateTimeOffset.UtcNow.AddDays(29), false));

        var calls = 0;
        _gateway.CaptureAuthorizationAsync(default!, default, default!, default!, default)
            .ReturnsForAnyArgs(_ =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new PaymentGatewayException("expired", 422, "UNPROCESSABLE_ENTITY", "AUTHORIZATION_EXPIRED");
                }
                return new GatewayCapture("CAP-2", "COMPLETED", 29m, 1.24m, 27.76m, "USD");
            });

        var service = CreateService();
        var result = await service.FulfilOrderAsync(1);

        Assert.Equal(OrderStatus.Paid, result.Status);
        Assert.Equal("CAP-2", result.Payment!.CaptureId);
        Assert.Equal("AUTH-2", result.Payment!.AuthorizationId); // renewed hold recorded
        await _gateway.Received(1).ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_reports_a_hold_that_can_no_longer_be_renewed()
    {
        var order = AuthorizedOrder();
        OrderRepoReturns(order);

        _gateway.CaptureAuthorizationAsync(default!, default, default!, default!, default)
            .ThrowsForAnyArgs(new PaymentGatewayException("expired", 422, "UNPROCESSABLE_ENTITY", "AUTHORIZATION_EXPIRED"));
        _gateway.ReauthorizeAsync(default!, default, default!, default!, default)
            .ThrowsForAnyArgs(new PaymentGatewayException("cannot reauthorize", 422, "UNPROCESSABLE_ENTITY", "REAUTHORIZATION_NOT_ALLOWED", "debug-123"));

        var service = CreateService();
        var ex = await Assert.ThrowsAsync<PaymentException>(() => service.FulfilOrderAsync(1));
        Assert.Contains("could not be renewed", ex.Message);
    }

    [Fact]
    public async Task Fulfil_is_idempotent_once_captured()
    {
        OrderRepoReturns(CapturedOrder());
        var service = CreateService();

        var result = await service.FulfilOrderAsync(1);

        Assert.Equal(OrderStatus.Paid, result.Status);
        await _gateway.DidNotReceiveWithAnyArgs().CaptureAuthorizationAsync(default!, default, default!, default!, default);
    }

    [Fact]
    public async Task Refund_with_a_repeated_key_does_not_refund_twice()
    {
        var order = CapturedOrder();
        order.Payment!.AddRefund("R1", 10m, "COMPLETED", "key-1");
        OrderRepoReturns(order);
        var service = CreateService();

        var refund = await service.RefundOrderAsync(Buyer, 1, 10m, "key-1");

        Assert.Equal("R1", refund.RefundId);
        await _gateway.DidNotReceiveWithAnyArgs().RefundCaptureAsync(default!, default, default!, default!, default);
    }

    [Fact]
    public async Task Refund_beyond_the_captured_amount_is_rejected()
    {
        OrderRepoReturns(CapturedOrder(29m));
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentException>(() => service.RefundOrderAsync(Buyer, 1, 30m, "key-x"));
        await _gateway.DidNotReceiveWithAnyArgs().RefundCaptureAsync(default!, default, default!, default!, default);
    }

    [Fact]
    public async Task Refund_calls_the_gateway_and_records_the_refund()
    {
        OrderRepoReturns(CapturedOrder(29m));
        _gateway.RefundCaptureAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayRefund("R-NEW", "COMPLETED", 10m, "USD"));
        var service = CreateService();

        var refund = await service.RefundOrderAsync(Buyer, 1, 10m, "key-1");

        Assert.Equal("R-NEW", refund.RefundId);
        Assert.Equal(10m, refund.Amount);
    }

    [Fact]
    public async Task Unknown_order_is_reported_as_not_found()
    {
        OrderRepoReturns(null);
        var service = CreateService();

        await Assert.ThrowsAsync<OrderNotFoundException>(() => service.FulfilOrderAsync(999));
    }
}
