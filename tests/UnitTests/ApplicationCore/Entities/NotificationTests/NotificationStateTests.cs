using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.NotificationTests;

public class NotificationStateTests
{
    private static Notification NewNotification() =>
        new(orderId: 7, ownerId: "buyer@example.com", NotificationKind.OrderPlaced, toNumber: "+15005550006", body: "hello");

    [Fact]
    public void NewNotificationIsPending()
    {
        var n = NewNotification();
        Assert.Equal(Notification.StatusPending, n.DeliveryStatus);
        Assert.Null(n.ProviderMessageSid);
        Assert.False(n.ContentDisposed);
    }

    [Fact]
    public void RecordProviderResultCapturesSidAndStatus()
    {
        var n = NewNotification();
        var sent = DateTimeOffset.UtcNow;
        n.RecordProviderResult("SM123", "delivered", errorCode: null, errorMessage: null, dateSent: sent);
        Assert.Equal("SM123", n.ProviderMessageSid);
        Assert.Equal("delivered", n.DeliveryStatus);
        Assert.Equal(sent, n.DateSent);
    }

    [Fact]
    public void RecordScheduledCapturesScheduleTime()
    {
        var n = NewNotification();
        var due = DateTimeOffset.UtcNow.AddDays(3);
        n.RecordScheduled("SM999", "scheduled", due);
        Assert.Equal("scheduled", n.DeliveryStatus);
        Assert.Equal(due, n.ScheduledFor);
    }

    [Fact]
    public void RecordSendFailedMarksTerminalFailureWithoutSid()
    {
        var n = NewNotification();
        n.RecordSendFailed("boom");
        Assert.Equal(Notification.StatusSendFailed, n.DeliveryStatus);
        Assert.Null(n.ProviderMessageSid);
    }

    [Fact]
    public void DisposeContentClearsBodyButKeepsStatusAndSid()
    {
        var n = NewNotification();
        n.RecordProviderResult("SM123", "delivered", null, null, DateTimeOffset.UtcNow);
        n.DisposeContent();
        Assert.Null(n.Body);
        Assert.True(n.ContentDisposed);
        Assert.Equal("SM123", n.ProviderMessageSid);
        Assert.Equal("delivered", n.DeliveryStatus);
    }

    [Fact]
    public void UpdateDeliveryStateRefreshesOutcome()
    {
        var n = NewNotification();
        n.RecordProviderResult("SM123", "queued", null, null, null);
        n.UpdateDeliveryState("undelivered", 30034, "carrier refused", DateTimeOffset.UtcNow);
        Assert.Equal("undelivered", n.DeliveryStatus);
        Assert.Equal(30034, n.ErrorCode);
    }
}
