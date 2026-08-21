using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class PaymentServiceTests
{
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<SavedPaymentMethod> _pmRepo = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IReadRepository<CatalogItem> _itemRepo = Substitute.For<IReadRepository<CatalogItem>>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IPayPalPaymentGateway _gateway = Substitute.For<IPayPalPaymentGateway>();
    private readonly PayPalSettings _settings = new() { Currency = "USD", Environment = "sandbox" };

    private const string Buyer = "shopper@example.com";

    private PaymentService CreateService() =>
        new(_orderRepo, _pmRepo, _itemRepo, _uriComposer, _gateway, _settings);

    private static Order NewAuthorizableOrder()
    {
        var items = new List<OrderItem>
        {
            new OrderItem(new CatalogItemOrdered(1, "Item", "pic.png"), 10m, 2) // total 20
        };
        return new Order(Buyer, new Address("s", "c", "st", "co", "z"), items);
    }

    private static Order FulfilledOrder(decimal captured = 20m)
    {
        var order = NewAuthorizableOrder();
        var payment = new Payment("po-1", "auth-1", "CREATED", captured, "USD");
        order.AttachAuthorization(payment);
        payment.RecordCapture("cap-1", "COMPLETED", captured, 1m, captured - 1m);
        order.MarkFulfilled();
        return order;
    }

    [Fact]
    public async Task Authorize_WithCard_PlacesHoldAndMarksAuthorized()
    {
        var order = NewAuthorizableOrder();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _gateway.AuthorizeAsync(Arg.Any<PayPalAuthorizationRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("po-1", "auth-1", "CREATED"));

        var card = new PayPalCardData("4111111111111111", "2030-01", "123", "Name", null);
        var result = await CreateService().AuthorizeOrderAsync(1, Buyer, card, null, default);

        Assert.Equal(OrderStatus.Authorized, result.Status);
        Assert.Equal("auth-1", result.Payment!.AuthorizationId);
        await _orderRepo.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_UnknownOrderForBuyer_Throws()
    {
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns((Order?)null);
        var card = new PayPalCardData("4111111111111111", "2030-01", "123", "Name", null);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            CreateService().AuthorizeOrderAsync(1, Buyer, card, null, default));
    }

    [Fact]
    public async Task Authorize_AlreadyAuthorized_IsIdempotent_NoSecondGatewayCall()
    {
        var order = NewAuthorizableOrder();
        order.AttachAuthorization(new Payment("po-1", "auth-1", "CREATED", 20m, "USD"));
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var card = new PayPalCardData("4111111111111111", "2030-01", "123", "Name", null);
        await CreateService().AuthorizeOrderAsync(1, Buyer, card, null, default);

        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<PayPalAuthorizationRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_WithSavedCard_UsesVaultId()
    {
        var order = NewAuthorizableOrder();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _pmRepo.FirstOrDefaultAsync(Arg.Any<SavedPaymentMethodByIdAndBuyerSpec>(), Arg.Any<CancellationToken>())
            .Returns(new SavedPaymentMethod(Buyer, "vault-9", "VISA", "1111", "2030-01", "Name"));
        _gateway.AuthorizeAsync(Arg.Any<PayPalAuthorizationRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("po-1", "auth-1", "CREATED"));

        await CreateService().AuthorizeOrderAsync(1, Buyer, null, 9, default);

        await _gateway.Received(1).AuthorizeAsync(
            Arg.Is<PayPalAuthorizationRequest>(r => r.VaultId == "vault-9" && r.Card == null),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_CapturesAndRecordsFeeAndNet()
    {
        var order = NewAuthorizableOrder();
        order.AttachAuthorization(new Payment("po-1", "auth-1", "CREATED", 20m, "USD"));
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdWithPaymentSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _gateway.CaptureAsync("auth-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("cap-1", "COMPLETED", 20m, 0.88m, 19.12m, "USD"));

        var result = await CreateService().FulfilOrderAsync(1, default);

        Assert.Equal(OrderStatus.Fulfilled, result.Status);
        Assert.Equal("cap-1", result.Payment!.CaptureId);
        Assert.Equal(0.88m, result.Payment.PayPalFee);
        Assert.Equal(19.12m, result.Payment.NetAmount);
    }

    [Fact]
    public async Task Fulfil_RenewsStaleAuthorization_ThenCaptures()
    {
        var order = NewAuthorizableOrder();
        order.AttachAuthorization(new Payment("po-1", "auth-1", "CREATED", 20m, "USD"));
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdWithPaymentSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        // The original hold is stale; the renewed hold captures cleanly.
        _gateway.CaptureAsync("auth-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AuthorizationExpiredException("hold expired"));
        _gateway.ReauthorizeAsync("auth-1", Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReauthorizationResult("auth-2", "CREATED"));
        _gateway.CaptureAsync("auth-2", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("cap-2", "COMPLETED", 20m, 1m, 19m, "USD"));

        var result = await CreateService().FulfilOrderAsync(1, default);

        Assert.Equal(OrderStatus.Fulfilled, result.Status);
        Assert.Equal("auth-2", result.Payment!.AuthorizationId);
        Assert.Equal("cap-2", result.Payment.CaptureId);
        await _gateway.Received(1).ReauthorizeAsync("auth-1", Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_NonRenewableStaleAuthorization_Surfaces()
    {
        var order = NewAuthorizableOrder();
        order.AttachAuthorization(new Payment("po-1", "auth-1", "CREATED", 20m, "USD"));
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdWithPaymentSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _gateway.CaptureAsync("auth-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AuthorizationExpiredException("hold expired"));
        _gateway.ReauthorizeAsync("auth-1", Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AuthorizationNotRenewableException("cannot renew"));

        await Assert.ThrowsAsync<AuthorizationNotRenewableException>(() =>
            CreateService().FulfilOrderAsync(1, default));
    }

    [Fact]
    public async Task Cancel_VoidsHoldAndMarksCancelled()
    {
        var order = NewAuthorizableOrder();
        order.AttachAuthorization(new Payment("po-1", "auth-1", "CREATED", 20m, "USD"));
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdWithPaymentSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateService().CancelOrderAsync(1, default);

        Assert.Equal(OrderStatus.Cancelled, result.Status);
        await _gateway.Received(1).VoidAsync("auth-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_SameIdempotencyKeyTwice_RefundsOnce()
    {
        var order = FulfilledOrder();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _gateway.RefundAsync("cap-1", Arg.Any<decimal?>(), "USD", "key-1", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("refund-1", "COMPLETED", 5m));

        await CreateService().RefundOrderAsync(1, Buyer, 5m, "key-1", default);
        await CreateService().RefundOrderAsync(1, Buyer, 5m, "key-1", default);

        await _gateway.Received(1).RefundAsync("cap-1", Arg.Any<decimal?>(), "USD", "key-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_TwoDistinctKeys_RefundTwice()
    {
        var order = FulfilledOrder();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _gateway.RefundAsync("cap-1", Arg.Any<decimal?>(), "USD", "key-a", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("refund-a", "COMPLETED", 5m));
        _gateway.RefundAsync("cap-1", Arg.Any<decimal?>(), "USD", "key-b", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("refund-b", "COMPLETED", 3m));

        await CreateService().RefundOrderAsync(1, Buyer, 5m, "key-a", default);
        var result = await CreateService().RefundOrderAsync(1, Buyer, 3m, "key-b", default);

        Assert.Equal(OrderStatus.PartiallyRefunded, result.Status);
        Assert.Equal(8m, result.Payment!.TotalRefunded());
    }

    [Fact]
    public async Task Refund_BeyondCapturedAmount_Rejected_NoGatewayCall()
    {
        var order = FulfilledOrder(captured: 20m);
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        await Assert.ThrowsAsync<PaymentStateException>(() =>
            CreateService().RefundOrderAsync(1, Buyer, 100m, "key-x", default));

        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_UnfulfilledOrder_Rejected()
    {
        var order = NewAuthorizableOrder();
        order.AttachAuthorization(new Payment("po-1", "auth-1", "CREATED", 20m, "USD")); // authorized, not captured
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        await Assert.ThrowsAsync<PaymentStateException>(() =>
            CreateService().RefundOrderAsync(1, Buyer, 5m, "key-1", default));
    }

    [Fact]
    public async Task DeleteCard_RemovesFromVaultAndRepo()
    {
        var saved = new SavedPaymentMethod(Buyer, "vault-9", "VISA", "1111", "2030-01", "Name");
        _pmRepo.FirstOrDefaultAsync(Arg.Any<SavedPaymentMethodByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns(saved);

        await CreateService().DeleteCardAsync(9, Buyer, default);

        await _gateway.Received(1).DeleteVaultedCardAsync("vault-9", Arg.Any<CancellationToken>());
        await _pmRepo.Received(1).DeleteAsync(saved, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteCard_NotOwnedByCaller_Throws()
    {
        _pmRepo.FirstOrDefaultAsync(Arg.Any<SavedPaymentMethodByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns((SavedPaymentMethod?)null);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            CreateService().DeleteCardAsync(9, Buyer, default));
        await _gateway.DidNotReceive().DeleteVaultedCardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
