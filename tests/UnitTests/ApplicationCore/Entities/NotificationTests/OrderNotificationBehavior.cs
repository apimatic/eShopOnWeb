using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.NotificationTests;

public class OrderNotificationBehavior
{
    private static OrderNotification NewNotification() =>
        new(orderId: 7, ownerId: "buyer@test", type: NotificationType.OrderPlaced, toNumber: "+15005550006", body: "hello");

    [Fact]
    public void NewNotificationStartsQueued()
    {
        var n = NewNotification();
        Assert.Equal(MessageDeliveryStatus.Queued, n.DeliveryStatus);
        Assert.False(n.IsScheduled);
        Assert.False(n.ContentRedacted);
        Assert.Null(n.ProviderMessageSid);
    }

    [Fact]
    public void MarkSentRecordsSidAndStatus()
    {
        var n = NewNotification();
        n.MarkSent("SM123", MessageDeliveryStatus.Delivered);
        Assert.Equal("SM123", n.ProviderMessageSid);
        Assert.Equal(MessageDeliveryStatus.Delivered, n.DeliveryStatus);
    }

    [Fact]
    public void MarkScheduledCapturesSendAt()
    {
        var n = NewNotification();
        var when = DateTimeOffset.UtcNow.AddDays(3);
        n.MarkScheduled("SM999", when, MessageDeliveryStatus.Scheduled);
        Assert.True(n.IsScheduled);
        Assert.Equal(when, n.ScheduledSendAt);
        Assert.Equal(MessageDeliveryStatus.Scheduled, n.DeliveryStatus);
    }

    [Fact]
    public void MarkSendFailedDoesNotThrowAndRecordsFailure()
    {
        var n = NewNotification();
        n.MarkSendFailed("network error");
        Assert.Equal(MessageDeliveryStatus.Failed, n.DeliveryStatus);
        Assert.Null(n.ProviderMessageSid);
    }

    [Fact]
    public void RedactClearsBodyButKeepsSidAndStatus()
    {
        var n = NewNotification();
        n.MarkSent("SM123", MessageDeliveryStatus.Undelivered);
        n.MarkContentRedacted();

        Assert.True(n.ContentRedacted);
        Assert.Null(n.Body);                                     // content disposed of
        Assert.Equal("SM123", n.ProviderMessageSid);             // the fact it was sent survives
        Assert.Equal(MessageDeliveryStatus.Undelivered, n.DeliveryStatus); // ...and what became of it
    }

    [Fact]
    public void MarkCanceledSetsCanceledStatus()
    {
        var n = NewNotification();
        n.MarkScheduled("SM999", DateTimeOffset.UtcNow.AddDays(3), MessageDeliveryStatus.Scheduled);
        n.MarkCanceled();
        Assert.Equal(MessageDeliveryStatus.Canceled, n.DeliveryStatus);
    }

    [Theory]
    [InlineData(MessageDeliveryStatus.Delivered, true)]
    [InlineData(MessageDeliveryStatus.Undelivered, true)]
    [InlineData(MessageDeliveryStatus.Failed, true)]
    [InlineData(MessageDeliveryStatus.Canceled, true)]
    [InlineData(MessageDeliveryStatus.Queued, false)]
    [InlineData(MessageDeliveryStatus.Scheduled, false)]
    [InlineData(MessageDeliveryStatus.Sent, false)]
    public void IsTerminalClassifiesOutcomes(string status, bool expected)
    {
        Assert.Equal(expected, MessageDeliveryStatus.IsTerminal(status));
    }
}
