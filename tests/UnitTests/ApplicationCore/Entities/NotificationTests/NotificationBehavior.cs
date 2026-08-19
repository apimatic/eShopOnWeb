using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.NotificationTests;

public class NotificationBehavior
{
    private static Notification NewNotification() =>
        new(orderId: 7, buyerId: "buyer@test.com", NotificationKind.OrderPlaced, "+15551234567", "hello");

    [Fact]
    public void StartsPendingWithNoProviderIdentifier()
    {
        var n = NewNotification();
        Assert.Equal(NotificationStatus.Pending, n.Status);
        Assert.Null(n.ProviderMessageSid);
    }

    [Fact]
    public void RecordProviderResultCapturesSidAndStatus()
    {
        var n = NewNotification();
        n.RecordProviderResult("SM123", NotificationStatus.Queued, null, null);
        Assert.Equal("SM123", n.ProviderMessageSid);
        Assert.Equal(NotificationStatus.Queued, n.Status);
    }

    [Fact]
    public void RecordSendFailureMarksFailedWithoutSid()
    {
        var n = NewNotification();
        n.RecordSendFailure("provider unreachable");
        Assert.Equal(NotificationStatus.Failed, n.Status);
        Assert.Null(n.ProviderMessageSid);
        Assert.True(n.CanBeResent());
    }

    [Fact]
    public void RedactionClearsBodyButKeepsRecord()
    {
        var n = NewNotification();
        n.RecordProviderResult("SM123", NotificationStatus.Delivered, null, null);
        n.MarkContentRedacted();
        Assert.Null(n.Body);
        Assert.True(n.ContentRedacted);
        Assert.Equal("SM123", n.ProviderMessageSid);
        Assert.Equal(NotificationStatus.Delivered, n.Status);
    }

    [Theory]
    [InlineData(NotificationStatus.Delivered, false)]
    [InlineData(NotificationStatus.Undelivered, true)]
    [InlineData(NotificationStatus.Failed, true)]
    [InlineData(NotificationStatus.Sent, false)]
    public void CanBeResentReflectsUndeliveredOutcomes(string status, bool expected)
    {
        var n = NewNotification();
        n.RecordProviderResult("SM123", status, null, null);
        Assert.Equal(expected, n.CanBeResent());
    }

    [Theory]
    [InlineData(NotificationStatus.Delivered, true)]
    [InlineData(NotificationStatus.Queued, false)]
    [InlineData(NotificationStatus.Canceled, true)]
    [InlineData(NotificationStatus.Sent, false)]
    public void IsTerminalReflectsSettledOutcomes(string status, bool expected)
    {
        Assert.Equal(expected, NotificationStatus.IsTerminal(status));
    }
}
