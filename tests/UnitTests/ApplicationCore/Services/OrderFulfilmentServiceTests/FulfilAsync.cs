using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderFulfilmentServiceTests;

public class FulfilAsync
{
    private readonly IRepository<Order> _mockOrderRepo = Substitute.For<IRepository<Order>>();
    private readonly IPaymentGateway _mockGateway = Substitute.For<IPaymentGateway>();
    private readonly IAppLogger<OrderFulfilmentService> _mockLogger = Substitute.For<IAppLogger<OrderFulfilmentService>>();

    private static Order CreateAuthorizedOrder()
    {
        var order = new Order("buyer@test.com", new Address("1 St", "City", "ST", "USA", "00000"),
            new List<OrderItem> { new(new CatalogItemOrdered(1, "Item", "pic.png"), 17.00m, 1) });
        var payment = new OrderPayment(order.Id, "USD", 17.00m, null, "paypal-order-1", "auth-1", "CREATED",
            DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-7));
        order.AttachPayment(payment);
        return order;
    }

    private OrderFulfilmentService CreateSut() => new(_mockOrderRepo, _mockGateway, _mockLogger);

    [Fact]
    public async Task CapturesFullOrderTotalAndMarksFulfilled()
    {
        var order = CreateAuthorizedOrder();
        _mockOrderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);
        _mockGateway.CaptureAsync("auth-1", 17.00m, "USD", true, Arg.Any<string>(), default)
            .Returns(new GatewayCaptureResult("capture-1", "COMPLETED", 17.00m, 0.93m, 16.07m, "USD", DateTimeOffset.UtcNow));

        var result = await CreateSut().FulfilAsync(order.Id);

        Assert.Equal(OrderStatus.Fulfilled, result.Status);
        Assert.Equal(17.00m, result.Payment!.CapturedAmount);
    }

    [Fact]
    public async Task RepeatingFulfilOnAlreadyFulfilledOrderDoesNotCaptureAgain()
    {
        var order = CreateAuthorizedOrder();
        order.Payment!.RecordCapture("capture-1", "COMPLETED", 17.00m, 0.93m, 16.07m, DateTimeOffset.UtcNow);
        order.MarkFulfilled();
        _mockOrderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        await CreateSut().FulfilAsync(order.Id);

        await _mockGateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
            Arg.Any<bool>(), Arg.Any<string>(), default);
    }

    [Fact]
    public async Task RenewsStaleAuthorizationThenCapturesSuccessfully()
    {
        var order = CreateAuthorizedOrder();
        _mockOrderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        var captureCallCount = 0;
        _mockGateway.CaptureAsync(Arg.Any<string>(), 17.00m, "USD", true, Arg.Any<string>(), default)
            .Returns(_ =>
            {
                captureCallCount++;
                if (captureCallCount == 1)
                    throw new PaymentGatewayException("Authorization has expired.", "UNPROCESSABLE_ENTITY", "debug-1",
                        new[] { "AUTHORIZATION_EXPIRED" });
                return new GatewayCaptureResult("capture-1", "COMPLETED", 17.00m, 0.93m, 16.07m, "USD", DateTimeOffset.UtcNow);
            });

        _mockGateway.ReauthorizeAsync("auth-1", 17.00m, "USD", Arg.Any<string>(), default)
            .Returns(new GatewayReauthorizationResult("auth-2", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));

        var result = await CreateSut().FulfilAsync(order.Id);

        Assert.Equal(OrderStatus.Fulfilled, result.Status);
        Assert.Equal("auth-2", result.Payment!.AuthorizationId);
        Assert.Equal(1, result.Payment.ReauthorizationCount);
        await _mockGateway.Received(2).CaptureAsync(Arg.Any<string>(), 17.00m, "USD", true, Arg.Any<string>(), default);
    }

    [Fact]
    public async Task SurfacesActionableErrorWhenAuthorizationCanNoLongerBeRenewed()
    {
        var order = CreateAuthorizedOrder();
        _mockOrderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        _mockGateway.CaptureAsync(Arg.Any<string>(), 17.00m, "USD", true, Arg.Any<string>(), default)
            .Returns<GatewayCaptureResult>(_ => throw new PaymentGatewayException("Authorization has expired.",
                "UNPROCESSABLE_ENTITY", "debug-1", new[] { "AUTHORIZATION_EXPIRED" }));

        _mockGateway.ReauthorizeAsync("auth-1", 17.00m, "USD", Arg.Any<string>(), default)
            .Returns<GatewayReauthorizationResult>(_ => throw new PaymentGatewayException(
                "Reauthorization window has passed.", "UNPROCESSABLE_ENTITY", "debug-2"));

        await Assert.ThrowsAsync<PaymentAuthorizationNotRenewableException>(() => CreateSut().FulfilAsync(order.Id));

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status); // fulfilment must not silently "succeed"
    }
}
