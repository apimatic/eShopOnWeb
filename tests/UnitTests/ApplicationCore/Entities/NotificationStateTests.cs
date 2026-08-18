using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities;

public class NotificationStateTests
{
    private static Notification New() =>
        new(orderId: 1, buyerId: "buyer", kind: NotificationKind.OrderPlaced, toNumber: "+15551234567", body: "hello");

    [Fact]
    public void StartsPendingWithNoSid()
    {
        var n = New();
        Assert.Equal(NotificationStatus.Pending, n.Status);
        Assert.Null(n.ProviderMessageSid);
    }

    [Fact]
    public void MarkSentCapturesSidAndStatus()
    {
        var n = New();
        n.MarkSent("SM123", NotificationStatus.Queued);
        Assert.Equal("SM123", n.ProviderMessageSid);
        Assert.Equal(NotificationStatus.Queued, n.Status);
    }

    [Fact]
    public void MarkSendFailedRecordsFailureWithoutSid()
    {
        var n = New();
        n.MarkSendFailed("provider unreachable");
        Assert.Equal(NotificationStatus.SendFailed, n.Status);
        Assert.Null(n.ProviderMessageSid);
        Assert.Equal("provider unreachable", n.ErrorMessage);
    }

    [Fact]
    public void RedactContentClearsBodyButKeepsTheRecord()
    {
        var n = New();
        n.MarkSent("SM123", NotificationStatus.Delivered);
        n.RedactContent();
        Assert.True(n.ContentRedacted);
        Assert.Equal(string.Empty, n.Body);
        // The record of the message and what became of it survives.
        Assert.Equal("SM123", n.ProviderMessageSid);
        Assert.Equal(NotificationStatus.Delivered, n.Status);
    }

    [Theory]
    [InlineData(NotificationStatus.Delivered, true)]
    [InlineData(NotificationStatus.Undelivered, true)]
    [InlineData(NotificationStatus.Failed, true)]
    [InlineData(NotificationStatus.Canceled, true)]
    [InlineData(NotificationStatus.Queued, false)]
    [InlineData(NotificationStatus.Scheduled, false)]
    [InlineData(NotificationStatus.Sent, false)]
    public void IsTerminalClassifiesStatuses(string status, bool expected)
    {
        Assert.Equal(expected, NotificationStatus.IsTerminal(status));
    }
}
