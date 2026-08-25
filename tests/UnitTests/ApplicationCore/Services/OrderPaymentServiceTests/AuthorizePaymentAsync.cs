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

public class AuthorizePaymentAsync
{
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _catalogRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<PaymentMethod> _paymentMethodRepo = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPayPalGateway _payPal = Substitute.For<IPayPalGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly PayPalOptions _options = new() { Currency = "USD", Environment = "sandbox" };

    private OrderPaymentService CreateSut() => new(_orderRepo, _catalogRepo, _paymentMethodRepo, _payPal, _uriComposer, _options);

    private static readonly PayPalCardDetails Card = new()
    {
        Number = "4111111111111111",
        Expiry = "2030-01",
        SecurityCode = "123",
        CardholderName = "Test Buyer",
        AddressLine1 = "1 Test St",
        City = "Testville",
        PostalCode = "12345",
        CountryCode = "US"
    };

    [Fact]
    public async Task AuthorizesWithCardAndAdvancesOrderStatus()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdWithPaymentSpecification>(), default).Returns(order);
        _payPal.AuthorizeCardPaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Card, Arg.Any<string?>(), default)
            .Returns(new PayPalAuthorizationResult
            {
                PayPalOrderId = "PP-ORDER-1",
                OrderStatus = "COMPLETED",
                AuthorizationId = "AUTH-1",
                AuthorizationStatus = "CREATED",
                CreateTime = DateTimeOffset.UtcNow,
                ExpirationTime = DateTimeOffset.UtcNow.AddDays(3)
            });

        var sut = CreateSut();
        var result = await sut.AuthorizePaymentAsync(order.BuyerId, order.Id, Card, null);

        Assert.Equal(OrderStatus.PaymentAuthorized, result.Status);
        Assert.Equal("AUTH-1", result.Payment!.AuthorizationId);
        await _orderRepo.Received(1).UpdateAsync(order, default);
    }

    [Fact]
    public async Task SecondCallIsIdempotentAndDoesNotCallPayPalAgain()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.BeginAuthorization(order.Total(), "USD", "PP-ORDER-1", "req-1", null, "AUTH-1", "CREATED", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3));
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdWithPaymentSpecification>(), default).Returns(order);

        var sut = CreateSut();
        var result = await sut.AuthorizePaymentAsync(order.BuyerId, order.Id, Card, null);

        Assert.Equal(OrderStatus.PaymentAuthorized, result.Status);
        await _payPal.DidNotReceiveWithAnyArgs().AuthorizeCardPaymentAsync(default, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task WrongBuyerThrowsForbidden()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdWithPaymentSpecification>(), default).Returns(order);

        var sut = CreateSut();
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => sut.AuthorizePaymentAsync("someone-else", order.Id, Card, null));
    }

    [Fact]
    public async Task MissingOrderThrowsNotFound()
    {
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdWithPaymentSpecification>(), default).Returns((Order?)null);

        var sut = CreateSut();
        await Assert.ThrowsAsync<OrderNotFoundException>(() => sut.AuthorizePaymentAsync("buyer", 999, Card, null));
    }

    [Fact]
    public async Task PayerActionRequiredThrowsPayPalActionRequired()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdWithPaymentSpecification>(), default).Returns(order);
        _payPal.AuthorizeCardPaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Card, Arg.Any<string?>(), default)
            .Returns(new PayPalAuthorizationResult { PayPalOrderId = "PP-ORDER-1", OrderStatus = "PAYER_ACTION_REQUIRED", RequiresPayerAction = true });

        var sut = CreateSut();
        await Assert.ThrowsAsync<PayPalActionRequiredException>(() => sut.AuthorizePaymentAsync(order.BuyerId, order.Id, Card, null));
    }

    [Fact]
    public async Task SavedCardBelongingToAnotherBuyerThrowsForbidden()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdWithPaymentSpecification>(), default).Returns(order);
        var paymentMethod = new PaymentMethod("someone-else", "cust-1", "token-1", "VISA", "1111", "2030-01");
        _paymentMethodRepo.GetByIdAsync(5, default).Returns(paymentMethod);

        var sut = CreateSut();
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => sut.AuthorizePaymentAsync(order.BuyerId, order.Id, null, 5));
    }
}
