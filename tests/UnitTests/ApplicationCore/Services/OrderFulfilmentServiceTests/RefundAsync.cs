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

public class RefundAsync
{
    private readonly IRepository<Order> _mockOrderRepo = Substitute.For<IRepository<Order>>();
    private readonly IPaymentGateway _mockGateway = Substitute.For<IPaymentGateway>();
    private readonly IAppLogger<OrderFulfilmentService> _mockLogger = Substitute.For<IAppLogger<OrderFulfilmentService>>();

    private static Order CreateFulfilledOrder(decimal capturedAmount)
    {
        var order = new Order("buyer@test.com", new Address("1 St", "City", "ST", "USA", "00000"),
            new List<OrderItem> { new(new CatalogItemOrdered(1, "Item", "pic.png"), capturedAmount, 1) });
        var payment = new OrderPayment(order.Id, "USD", capturedAmount, null, "paypal-order-1", "auth-1", "CREATED",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3));
        order.AttachPayment(payment);
        payment.RecordCapture("capture-1", "COMPLETED", capturedAmount, 1.00m, capturedAmount - 1.00m, DateTimeOffset.UtcNow);
        order.MarkFulfilled();
        return order;
    }

    private OrderFulfilmentService CreateSut() => new(_mockOrderRepo, _mockGateway, _mockLogger);

    [Fact]
    public async Task RepeatingSameIdempotencyKeyDoesNotCallGatewayAgain()
    {
        var order = CreateFulfilledOrder(17.00m);
        _mockOrderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);
        _mockGateway.RefundAsync("capture-1", 5.00m, "USD", Arg.Any<string?>(), Arg.Any<string>(), default)
            .Returns(new GatewayRefundResult("refund-1", "COMPLETED", 5.00m, "USD"));

        var sut = CreateSut();
        var first = await sut.RefundAsync(order.Id, 5.00m, "key-1", null);
        var second = await sut.RefundAsync(order.Id, 5.00m, "key-1", null);

        Assert.Equal(first.Refund.PayPalRefundId, second.Refund.PayPalRefundId);
        await _mockGateway.Received(1).RefundAsync("capture-1", 5.00m, "USD", Arg.Any<string?>(), Arg.Any<string>(), default);
    }

    [Fact]
    public async Task DistinctIdempotencyKeysProduceTwoLegitimatePartialRefunds()
    {
        var order = CreateFulfilledOrder(17.00m);
        _mockOrderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);
        _mockGateway.RefundAsync("capture-1", 5.00m, "USD", Arg.Any<string?>(), Arg.Any<string>(), default)
            .Returns(new GatewayRefundResult("refund-1", "COMPLETED", 5.00m, "USD"));
        _mockGateway.RefundAsync("capture-1", 10.00m, "USD", Arg.Any<string?>(), Arg.Any<string>(), default)
            .Returns(new GatewayRefundResult("refund-2", "COMPLETED", 10.00m, "USD"));

        var sut = CreateSut();
        await sut.RefundAsync(order.Id, 5.00m, "key-1", null);
        await sut.RefundAsync(order.Id, 10.00m, "key-2", null);

        Assert.Equal(15.00m, order.Payment!.TotalRefunded);
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
    }

    [Fact]
    public async Task RefundExceedingRemainingAmountThrows()
    {
        var order = CreateFulfilledOrder(17.00m);
        _mockOrderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        var sut = CreateSut();
        await Assert.ThrowsAsync<RefundAmountExceedsRemainingException>(
            () => sut.RefundAsync(order.Id, 20.00m, "key-1", null));

        await _mockGateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string>(), default);
    }

    [Fact]
    public async Task SecondRefundExceedingWhatFirstOneLeftThrows()
    {
        var order = CreateFulfilledOrder(17.00m);
        _mockOrderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);
        _mockGateway.RefundAsync("capture-1", 10.00m, "USD", Arg.Any<string?>(), Arg.Any<string>(), default)
            .Returns(new GatewayRefundResult("refund-1", "COMPLETED", 10.00m, "USD"));

        var sut = CreateSut();
        await sut.RefundAsync(order.Id, 10.00m, "key-1", null);

        // 17.00 captured - 10.00 already refunded = 7.00 remaining; 10.00 must be rejected.
        await Assert.ThrowsAsync<RefundAmountExceedsRemainingException>(
            () => sut.RefundAsync(order.Id, 10.00m, "key-2", null));
    }
}
