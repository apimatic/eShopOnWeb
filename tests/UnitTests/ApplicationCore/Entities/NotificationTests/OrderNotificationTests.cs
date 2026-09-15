using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.NotificationTests;

public class OrderNotificationTests
{
    private static OrderNotification New()
        => new(orderId: 7, buyerId: "buyer@test.com", NotificationType.OrderPlaced, "+15551234567", contactNumberId: 3);

    [Fact]
    public void StartsPending()
    {
        var n = New();
        Assert.Equal(NotificationDeliveryStatus.Pending, n.DeliveryStatus);
        Assert.Null(n.ProviderMessageSid);
        Assert.False(n.ContentRedacted);
    }

    [Fact]
    public void RecordAcceptedCapturesProviderState()
    {
        var n = New();
        var at = DateTimeOffset.UtcNow.AddDays(3);
        n.RecordAccepted("SM123", NotificationDeliveryStatus.Scheduled, null, null, at);

        Assert.Equal("SM123", n.ProviderMessageSid);
        Assert.Equal(NotificationDeliveryStatus.Scheduled, n.DeliveryStatus);
        Assert.Equal(at, n.ScheduledSendAt);
        Assert.True(n.IsScheduled());
    }

    [Fact]
    public void RecordSendFailedHasNoSidAndIsUndelivered()
    {
        var n = New();
        n.RecordSendFailed("provider refused");
        Assert.Equal(NotificationDeliveryStatus.SendFailed, n.DeliveryStatus);
        Assert.Null(n.ProviderMessageSid);
        Assert.True(n.IsUndelivered());
    }

    [Fact]
    public void UpdateDeliveryStatusReflectsTerminalOutcome()
    {
        var n = New();
        n.RecordAccepted("SM1", NotificationDeliveryStatus.Queued, null, null, null);
        n.UpdateDeliveryStatus(NotificationDeliveryStatus.Undelivered, 30006, "unreachable");
        Assert.Equal(NotificationDeliveryStatus.Undelivered, n.DeliveryStatus);
        Assert.Equal(30006, n.ErrorCode);
        Assert.True(n.IsUndelivered());
    }

    [Fact]
    public void MarkCancelledSetsCanceled()
    {
        var n = New();
        n.RecordAccepted("SM1", NotificationDeliveryStatus.Scheduled, null, null, DateTimeOffset.UtcNow.AddDays(3));
        n.MarkCancelled();
        Assert.Equal(NotificationDeliveryStatus.Canceled, n.DeliveryStatus);
        Assert.False(n.IsScheduled());
    }

    [Fact]
    public void MarkContentRedactedPreservesOutcome()
    {
        var n = New();
        n.RecordAccepted("SM1", NotificationDeliveryStatus.Delivered, null, null, null);
        n.MarkContentRedacted();
        Assert.True(n.ContentRedacted);
        Assert.Equal(NotificationDeliveryStatus.Delivered, n.DeliveryStatus);
        Assert.Equal("SM1", n.ProviderMessageSid);
    }
}
