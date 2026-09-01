using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class FulfilOrder : OrderPaymentServiceTestBase
{
    [Fact]
    public async Task CapturesAndRecordsPayPalFeeAndNet()
    {
        var order = NewAuthorizedOrder();
        ReturnsOrder(order);
        Gateway.CaptureAuthorizationAsync("AUTH-1", 24m, "USD", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP-1", "COMPLETED", 24m, 1.11m, 22.89m, "USD"));

        var result = await CreateService().FulfilOrderAsync(1);

        Assert.Equal(OrderPaymentStatus.Captured, result.PaymentStatus);
        Assert.Equal("CAP-1", result.CaptureId);
        Assert.Equal(24m, result.CapturedGrossAmount);
        Assert.Equal(1.11m, result.CapturedFeeAmount);
        Assert.Equal(22.89m, result.CapturedNetAmount);
    }

    [Fact]
    public async Task RepeatedFulfilDoesNotCaptureTwice()
    {
        var order = NewCapturedOrder();
        ReturnsOrder(order);

        var result = await CreateService().FulfilOrderAsync(1);

        Assert.Equal("CAP-1", result.CaptureId);
        await Gateway.DidNotReceive().CaptureAuthorizationAsync(Arg.Any<string>(), Arg.Any<decimal>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StaleAuthorizationIsRenewedThenCaptured()
    {
        var order = NewAuthorizedOrder(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));
        ReturnsOrder(order);
        Gateway.ReauthorizePaymentAsync("AUTH-1", 24m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationInfo("AUTH-2", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
        Gateway.CaptureAuthorizationAsync("AUTH-2", 24m, "USD", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP-2", "COMPLETED", 24m, 1.11m, 22.89m, "USD"));

        var result = await CreateService().FulfilOrderAsync(1);

        Assert.Equal(OrderPaymentStatus.Captured, result.PaymentStatus);
        Assert.Equal("AUTH-2", result.AuthorizationId);
        Assert.Equal("CAP-2", result.CaptureId);
        await Gateway.Received(1).ReauthorizePaymentAsync("AUTH-1", 24m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthorizationOlderThanThirtyDaysReportsNonRenewable()
    {
        var order = NewAuthorizedOrder(expiresAt: DateTimeOffset.UtcNow.AddDays(-28));
        SetAuthorizedAt(order, DateTimeOffset.UtcNow.AddDays(-31));
        ReturnsOrder(order);

        var ex = await Assert.ThrowsAsync<PaymentStateException>(() => CreateService().FulfilOrderAsync(1));

        Assert.Contains("can no longer be renewed", ex.Message);
        await Gateway.DidNotReceive().ReauthorizePaymentAsync(Arg.Any<string>(), Arg.Any<decimal>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FulfilWithoutAuthorizationConflicts()
    {
        ReturnsOrder(NewOrder());

        await Assert.ThrowsAsync<PaymentStateException>(() => CreateService().FulfilOrderAsync(1));
    }
}
