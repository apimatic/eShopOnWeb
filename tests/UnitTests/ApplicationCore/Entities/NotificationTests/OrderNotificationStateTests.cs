using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.NotificationTests;

public class OrderNotificationStateTests
{
    private static OrderNotification NewNotification() =>
        new(orderId: 1, buyerId: "buyer@example.com", NotificationType.OrderPlaced, toNumber: "+15005550006", body: "hello");

    [Fact]
    public void RecordAcceptedCapturesProviderIdentityAndStatus()
    {
        var n = NewNotification();
        n.RecordAccepted("SM123", "queued");

        Assert.Equal("SM123", n.ProviderSid);
        Assert.Equal("queued", n.ProviderStatus);
        Assert.False(n.IsScheduled);
    }

    [Fact]
    public void RecordAcceptedScheduledMarksScheduled()
    {
        var when = DateTimeOffset.UtcNow.AddDays(3);
        var n = NewNotification();
        n.RecordAccepted("SM123", "scheduled", isScheduled: true, scheduledSendAt: when);

        Assert.True(n.IsScheduled);
        Assert.Equal(when, n.ScheduledSendAt);
    }

    [Fact]
    public void ApplyProviderStateClearsScheduledOnceSent()
    {
        var n = NewNotification();
        n.RecordAccepted("SM123", "scheduled", isScheduled: true, scheduledSendAt: DateTimeOffset.UtcNow.AddDays(3));
        n.ApplyProviderState("delivered", null, null, DateTimeOffset.UtcNow);

        Assert.Equal("delivered", n.ProviderStatus);
        Assert.False(n.IsScheduled);
    }

    [Fact]
    public void MarkCanceledSetsCanceledAndClearsScheduled()
    {
        var n = NewNotification();
        n.RecordAccepted("SM123", "scheduled", isScheduled: true, scheduledSendAt: DateTimeOffset.UtcNow.AddDays(3));
        n.MarkCanceled();

        Assert.Equal("canceled", n.ProviderStatus);
        Assert.False(n.IsScheduled);
    }

    [Fact]
    public void DisposeContentClearsBodyButKeepsProviderState()
    {
        var n = NewNotification();
        n.RecordAccepted("SM123", "delivered");
        n.DisposeContent();

        Assert.Null(n.Body);
        Assert.True(n.ContentDisposed);
        Assert.Equal("SM123", n.ProviderSid);
        Assert.Equal("delivered", n.ProviderStatus);
    }

    [Fact]
    public void RecordSendFailureIsNonFatalAndRecorded()
    {
        var n = NewNotification();
        n.RecordSendFailure("provider rejected");

        Assert.Equal("send_failed", n.ProviderStatus);
        Assert.Null(n.ProviderSid);
    }

    [Fact]
    public void SetIdempotencyKeyStamps()
    {
        var n = NewNotification();
        n.SetIdempotencyKey("key-1");
        Assert.Equal("key-1", n.IdempotencyKey);
    }
}
