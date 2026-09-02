using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class PaymentServiceTests
{
    private readonly IRepository<Order> _mockOrderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _mockPaymentRepo = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<SavedPaymentMethod> _mockSavedRepo = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IPayPalClient _mockPayPal = Substitute.For<IPayPalClient>();
    private readonly PayPalSettings _settings = new PayPalSettings { Currency = "USD", Environment = "sandbox" };

    private PaymentService CreateService() =>
        new PaymentService(_mockOrderRepo, _mockPaymentRepo, _mockSavedRepo, _mockPayPal, _settings);

    private static Order CreateOrder()
    {
        var items = new List<OrderItem>
        {
            new OrderItem(new CatalogItemOrdered(1, "T-Shirt", "uri"), 12.00m, 2)
        };
        return new Order("buyer@example.com", new Address("street", "city", "state", "country", "zip"), items);
    }

    private Payment CreateAuthorizedPayment(Order order)
    {
        var payment = new Payment(order.Id, order.BuyerId, order.Total(), "USD");
        payment.SetPayPalOrderId("PAYPAL-ORDER-1");
        payment.MarkAuthorized("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), null);
        return payment;
    }

    [Fact]
    public async Task AuthorizeTwiceReturnsExistingPaymentWithoutCallingPayPalAgain()
    {
        var order = CreateOrder();
        var existing = CreateAuthorizedPayment(order);
        _mockPaymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var service = CreateService();
        var result = await service.AuthorizePaymentAsync(order, new CardDetails { Number = "4111111111111111" }, null);

        Assert.Same(existing, result);
        await _mockPayPal.DidNotReceive().CreateOrderAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _mockPayPal.DidNotReceive().AuthorizeOrderWithCardAsync(Arg.Any<string>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CaptureTwiceNeverCapturesTwice()
    {
        var order = CreateOrder();
        var payment = CreateAuthorizedPayment(order);
        payment.MarkCaptured("CAPTURE-1", 24m, 1m, 23m);
        _mockPaymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(payment);

        var service = CreateService();
        var result = await service.CapturePaymentAsync(order);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        await _mockPayPal.DidNotReceive().CaptureAuthorizationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CaptureRenewsStaleAuthorizationBeforeCapturing()
    {
        var order = CreateOrder();
        order.MarkPaid();
        var payment = CreateAuthorizedPayment(order);
        _mockPaymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(payment);

        // Authorization is no longer in CREATED state (stale) but can be reauthorized.
        _mockPayPal.GetAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizationResult { AuthorizationId = "AUTH-1", Status = "EXPIRED" });
        _mockPayPal.ReauthorizeAsync("AUTH-1", payment.Amount, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizationResult { AuthorizationId = "AUTH-1", Status = "CREATED" });
        _mockPayPal.CaptureAuthorizationAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCaptureResult { CaptureId = "CAPTURE-9", Status = "COMPLETED", Amount = 24m, Currency = "USD", PayPalFee = 1m, NetAmount = 23m });

        var service = CreateService();
        var result = await service.CapturePaymentAsync(order);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        Assert.Equal("CAPTURE-9", result.CaptureId);
        await _mockPayPal.Received(1).ReauthorizeAsync("AUTH-1", payment.Amount, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CaptureReportsActionableErrorWhenAuthorizationCannotBeRenewed()
    {
        var order = CreateOrder();
        var payment = CreateAuthorizedPayment(order);
        _mockPaymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(payment);

        _mockPayPal.GetAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizationResult { AuthorizationId = "AUTH-1", Status = "EXPIRED" });
        _mockPayPal.ReauthorizeAsync("AUTH-1", Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<PayPalAuthorizationResult>>(_ => throw new PayPalApiException(System.Net.HttpStatusCode.UnprocessableEntity, "UNPROCESSABLE_ENTITY", "cannot reauthorize", "dbg"));

        var service = CreateService();

        var ex = await Assert.ThrowsAsync<AuthorizationRenewalException>(() => service.CapturePaymentAsync(order));
        Assert.Contains("could not be renewed", ex.Message);
        Assert.Contains("pay again", ex.Message);
    }

    [Fact]
    public async Task RefundWithRepeatedIdempotencyKeyReturnsOriginalRefund()
    {
        var order = CreateOrder();
        var payment = CreateAuthorizedPayment(order);
        payment.MarkCaptured("CAPTURE-1", 24m, 1m, 23m);
        payment.AddRefund("REF-1", 10m, "key-1", "COMPLETED");
        _mockPaymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(payment);

        var service = CreateService();
        var result = await service.RefundPaymentAsync(order, 10m, "key-1");

        Assert.Equal("REF-1", result.PayPalRefundId);
        await _mockPayPal.DidNotReceive().RefundCaptureAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundBeyondCapturedAmountIsRejected()
    {
        var order = CreateOrder();
        var payment = CreateAuthorizedPayment(order);
        payment.MarkCaptured("CAPTURE-1", 24m, 1m, 23m);
        _mockPaymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(payment);

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidPaymentStateException>(() => service.RefundPaymentAsync(order, 25m, "key-9"));
    }
}
