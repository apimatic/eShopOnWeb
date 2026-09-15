using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.UnitTests.Builders;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class PaymentServiceTests
{
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _itemRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IReadRepository<PaymentMethod> _pmRepo = Substitute.For<IReadRepository<PaymentMethod>>();
    private readonly IPayPalPaymentGateway _gateway = Substitute.For<IPayPalPaymentGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<PaymentService> _logger = Substitute.For<IAppLogger<PaymentService>>();

    private PaymentService CreateService() =>
        new(_orderRepo, _itemRepo, _pmRepo, _gateway, _uriComposer,
            Options.Create(new PayPalSettings { Currency = "USD" }), _logger);

    private static CardDetails TestCard() => new("4111111111111111", "2030-01", "123", "John Doe", null);

    private static Order AuthorizedOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        var payment = new Payment("PPORDER1", "USD", order.Total(), "VISA", "1111");
        payment.RecordAuthorization("AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(29));
        order.SetAuthorized(payment);
        return order;
    }

    [Fact]
    public async Task AuthorizeWithCardRecordsHoldAndSavesOnce()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _gateway.AuthorizeWithCardAsync(Arg.Any<Money>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CardAuthorizationResult("PP1", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), "VISA", "1111", "2030-01"));

        var service = CreateService();
        await service.AuthorizeOrderAsync(order, new PaymentInstruction { Card = TestCard() });

        Assert.Equal(OrderStatus.Authorized, order.Status);
        Assert.Equal("AUTH1", order.Payment!.AuthorizationId);
        await _gateway.Received(1).AuthorizeWithCardAsync(Arg.Any<Money>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _orderRepo.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthorizeIsIdempotentForAlreadyAuthorizedOrder()
    {
        var order = AuthorizedOrder();

        var service = CreateService();
        await service.AuthorizeOrderAsync(order, new PaymentInstruction { Card = TestCard() });

        // No second hold placed.
        await _gateway.DidNotReceive().AuthorizeWithCardAsync(Arg.Any<Money>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthorizeWithAnotherShoppersSavedCardIsNotFound()
    {
        var order = new OrderBuilder().WithDefaultValues(); // buyer "12345"
        var foreignCard = new PaymentMethod("someone-else", "VAULT1", null, "VISA", "1111", "2030-01", "X", null);
        _pmRepo.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(foreignCard);

        var service = CreateService();

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            service.AuthorizeOrderAsync(order, new PaymentInstruction { SavedPaymentMethodId = 5 }));
        await _gateway.DidNotReceive().AuthorizeWithVaultedCardAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FulfilCapturesAndRecordsBreakdown()
    {
        var order = AuthorizedOrder();
        _gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP1", "COMPLETED", order.Total(), 1.72m, order.Total() - 1.72m, "USD"));

        var service = CreateService();
        await service.FulfilOrderAsync(order);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal("CAP1", order.Payment!.CaptureId);
        Assert.Equal(1.72m, order.Payment.PayPalFee);
    }

    [Fact]
    public async Task FulfilRenewsStaleAuthorizationThenCaptures()
    {
        var order = AuthorizedOrder();
        int captureCalls = 0;
        _gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                captureCalls++;
                if (captureCalls == 1)
                {
                    throw new PaymentGatewayException("PayPal request failed (422) [AUTHORIZATION_EXPIRED]");
                }
                return new CaptureResult("CAP2", "COMPLETED", order.Total(), 1.72m, order.Total() - 1.72m, "USD");
            });
        _gateway.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>())
            .Returns(new ReauthorizationResult("AUTH2", "CREATED", DateTimeOffset.UtcNow.AddDays(29)));

        var service = CreateService();
        await service.FulfilOrderAsync(order);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal("CAP2", order.Payment!.CaptureId);
        Assert.Equal("AUTH2", order.Payment.AuthorizationId); // renewed hold id retained
        await _gateway.Received(1).ReauthorizeAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FulfilSurfacesOperatorActionableErrorWhenRenewalImpossible()
    {
        var order = AuthorizedOrder();
        _gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<CaptureResult>(_ => throw new PaymentGatewayException("capture failed: expired"));
        _gateway.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>())
            .Returns<ReauthorizationResult>(_ => throw new PaymentGatewayException("REAUTHORIZATION window closed"));

        var service = CreateService();

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => service.FulfilOrderAsync(order));
        Assert.Contains("pay for the order again", ex.Message);
        Assert.NotEqual(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public async Task RefundIsIdempotentForSameKey()
    {
        var order = AuthorizedOrder();
        order.SetFulfilled("CAP1", "COMPLETED", order.Total(), 1.72m, order.Total() - 1.72m);
        _gateway.RefundAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult("REF1", "COMPLETED", 1m, "USD"));

        var service = CreateService();
        var first = await service.RefundOrderAsync(order, 1m, "key-1");
        var second = await service.RefundOrderAsync(order, 1m, "key-1");

        Assert.Equal(first.PayPalRefundId, second.PayPalRefundId);
        Assert.Single(order.Payment!.Refunds);
        await _gateway.Received(1).RefundAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundBeyondCapturedAmountIsRejectedWithoutCallingGateway()
    {
        var order = AuthorizedOrder();
        var total = order.Total();
        order.SetFulfilled("CAP1", "COMPLETED", total, 0m, total);

        var service = CreateService();

        await Assert.ThrowsAsync<PaymentException>(() => service.RefundOrderAsync(order, total + 5m, "key-x"));
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
