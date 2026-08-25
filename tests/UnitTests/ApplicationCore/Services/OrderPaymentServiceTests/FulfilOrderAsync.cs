using System;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class FulfilOrderAsync
{
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _catalogRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<PaymentMethod> _paymentMethodRepo = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPayPalGateway _payPal = Substitute.For<IPayPalGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly PayPalOptions _options = new() { Currency = "USD", Environment = "sandbox" };

    private OrderPaymentService CreateSut() => new(_orderRepo, _catalogRepo, _paymentMethodRepo, _payPal, _uriComposer, _options);

    private Order AuthorizedOrder(DateTimeOffset authorizedAt, DateTimeOffset? expiresAt)
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.BeginAuthorization(order.Total(), "USD", "PP-ORDER-1", "req-1", null, "AUTH-1", "CREATED", authorizedAt, expiresAt);
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdWithPaymentSpecification>(), default).Returns(order);
        return order;
    }

    [Fact]
    public async Task FreshAuthorizationCapturesDirectlyWithoutReauthorizing()
    {
        var now = DateTimeOffset.UtcNow;
        var order = AuthorizedOrder(now, now.AddDays(3));
        _payPal.CaptureAuthorizationAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), default)
            .Returns(new PayPalCaptureResult { CaptureId = "CAP-1", Status = "COMPLETED", GrossAmount = order.Total(), FeeAmount = 0.5m, NetAmount = order.Total() - 0.5m, CurrencyCode = "USD", CaptureTime = now });

        var sut = CreateSut();
        var result = await sut.FulfilOrderAsync(order.Id);

        Assert.Equal(OrderStatus.Fulfilled, result.Status);
        Assert.Equal("CAP-1", result.Payment!.CaptureId);
        await _payPal.DidNotReceiveWithAnyArgs().ReauthorizeAsync(default!, default, default!, default!, default);
    }

    [Fact]
    public async Task StaleAuthorizationWithinWindowReauthorizesThenCaptures()
    {
        var originalAuthTime = DateTimeOffset.UtcNow.AddDays(-10);
        var order = AuthorizedOrder(originalAuthTime, originalAuthTime.AddDays(3)); // expired 7 days ago
        _payPal.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), default)
            .Returns(new PayPalReauthorizationResult { AuthorizationId = "AUTH-2", Status = "CREATED", CreateTime = DateTimeOffset.UtcNow, ExpirationTime = DateTimeOffset.UtcNow.AddDays(3) });
        _payPal.CaptureAuthorizationAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), default)
            .Returns(new PayPalCaptureResult { CaptureId = "CAP-1", Status = "COMPLETED", GrossAmount = order.Total(), FeeAmount = 0m, NetAmount = order.Total(), CurrencyCode = "USD", CaptureTime = DateTimeOffset.UtcNow });

        var sut = CreateSut();
        var result = await sut.FulfilOrderAsync(order.Id);

        Assert.Equal(OrderStatus.Fulfilled, result.Status);
        Assert.Equal("AUTH-2", result.Payment!.AuthorizationId);
        Assert.Equal("CAP-1", result.Payment.CaptureId);
    }

    [Fact]
    public async Task AuthorizationPastMaxWindowFailsWithoutCallingPayPal()
    {
        var originalAuthTime = DateTimeOffset.UtcNow.AddDays(-40); // beyond PayPal's 29-day reauthorization ceiling
        AuthorizedOrder(originalAuthTime, originalAuthTime.AddDays(3));

        var sut = CreateSut();
        await Assert.ThrowsAsync<PayPalOperationException>(() => sut.FulfilOrderAsync(1));
        await _payPal.DidNotReceiveWithAnyArgs().ReauthorizeAsync(default!, default, default!, default!, default);
    }

    [Fact]
    public async Task AlreadyFulfilledIsIdempotent()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.BeginAuthorization(order.Total(), "USD", "PP-ORDER-1", "req-1", null, "AUTH-1", "CREATED", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3));
        order.MarkFulfilled("CAP-1", "COMPLETED", order.Total(), 0m, order.Total(), "cap-req", DateTimeOffset.UtcNow);
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdWithPaymentSpecification>(), default).Returns(order);

        var sut = CreateSut();
        var result = await sut.FulfilOrderAsync(order.Id);

        Assert.Equal(OrderStatus.Fulfilled, result.Status);
        await _payPal.DidNotReceiveWithAnyArgs().CaptureAuthorizationAsync(default!, default, default!, default!, default);
    }

    [Fact]
    public async Task NeverAuthorizedThrowsInvalidState()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdWithPaymentSpecification>(), default).Returns(order);

        var sut = CreateSut();
        await Assert.ThrowsAsync<InvalidOrderStateException>(() => sut.FulfilOrderAsync(order.Id));
    }
}
