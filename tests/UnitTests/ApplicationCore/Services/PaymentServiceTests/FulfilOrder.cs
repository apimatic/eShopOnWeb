using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class FulfilOrder : PaymentServiceTestBase
{
    [Fact]
    public async Task CapturesAndReportsFeeAndNet()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();
        var payment = NewAuthorizedPayment();
        GivenOrder(order);
        GivenPayment(payment);
        Gateway.GetAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorizationStatus("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), 20m, "USD"));
        Gateway.CaptureAsync("AUTH-1", OrderId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayCaptureResult("CAP-1", "COMPLETED", 20m, 1.10m, 18.90m, "USD"));

        var result = await CreateService().FulfilOrderAsync(OrderId, CancellationToken.None);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(PaymentStatus.Captured, result.Status);
        Assert.Equal("CAP-1", result.CaptureId);
        Assert.Equal(20m, result.CapturedAmount);
        Assert.Equal(1.10m, result.PayPalFee);
        Assert.Equal(18.90m, result.NetAmount);
    }

    [Fact]
    public async Task RenewsStaleAuthorizationBeforeCapturing()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();
        var payment = NewAuthorizedPayment();
        GivenOrder(order);
        GivenPayment(payment);
        Gateway.GetAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorizationStatus("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(-1), 20m, "USD"));
        Gateway.ReauthorizeAsync("AUTH-1", 20m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorizationStatus("AUTH-2", "CREATED", DateTimeOffset.UtcNow.AddDays(3), 20m, "USD"));
        Gateway.CaptureAsync("AUTH-2", OrderId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayCaptureResult("CAP-2", "COMPLETED", 20m, 1.10m, 18.90m, "USD"));

        var result = await CreateService().FulfilOrderAsync(OrderId, CancellationToken.None);

        Assert.Equal("AUTH-2", result.AuthorizationId);
        Assert.Equal("CAP-2", result.CaptureId);
        await Gateway.Received(1).ReauthorizeAsync("AUTH-1", 20m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await Gateway.Received(1).CaptureAsync("AUTH-2", OrderId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenewsWhenAuthorizationCanNoLongerBeRead()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();
        var payment = NewAuthorizedPayment();
        GivenOrder(order);
        GivenPayment(payment);
        Gateway.GetAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>())
            .Throws(new PaymentGatewayException("not found", 404, "RESOURCE_NOT_FOUND", null));
        Gateway.ReauthorizeAsync("AUTH-1", 20m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorizationStatus("AUTH-2", "CREATED", DateTimeOffset.UtcNow.AddDays(3), 20m, "USD"));
        Gateway.CaptureAsync("AUTH-2", OrderId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayCaptureResult("CAP-2", "COMPLETED", 20m, 1.10m, 18.90m, "USD"));

        var result = await CreateService().FulfilOrderAsync(OrderId, CancellationToken.None);

        Assert.Equal("CAP-2", result.CaptureId);
    }

    [Fact]
    public async Task ThrowsActionableErrorWhenAuthorizationCannotBeRenewed()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();
        var payment = NewAuthorizedPayment();
        GivenOrder(order);
        GivenPayment(payment);
        Gateway.GetAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorizationStatus("AUTH-1", "DENIED", null, 20m, "USD"));
        Gateway.ReauthorizeAsync("AUTH-1", 20m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new PaymentGatewayException("cannot reauthorize", 422, "NOT_REAUTHORIZABLE", null));

        await Assert.ThrowsAsync<AuthorizationNotRenewableException>(
            () => CreateService().FulfilOrderAsync(OrderId, CancellationToken.None));

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(PaymentStatus.RequiresNewAuthorization, payment.Status);
        await Gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IsIdempotentWhenAlreadyFulfilled()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();
        var payment = NewAuthorizedPayment();
        payment.MarkCaptured("CAP-1", 20m, 1.10m, 18.90m);
        order.MarkFulfilled();
        GivenOrder(order);
        GivenPayment(payment);

        var result = await CreateService().FulfilOrderAsync(OrderId, CancellationToken.None);

        Assert.Equal("CAP-1", result.CaptureId);
        await Gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsFulfilmentOfUnpaidOrder()
    {
        GivenOrder(NewOrder());
        GivenPayment(null);

        await Assert.ThrowsAsync<OrderStateException>(
            () => CreateService().FulfilOrderAsync(OrderId, CancellationToken.None));
    }
}
