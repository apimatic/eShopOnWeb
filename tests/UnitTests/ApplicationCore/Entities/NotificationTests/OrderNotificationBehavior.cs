using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.NotificationTests;

public class OrderNotificationBehavior
{
    [Fact]
    public void NewNotificationStartsNotSent()
    {
        var n = OrderNotification.Create(1, "buyer", NotificationType.OrderPlaced, "+15551230000", "hi");
        Assert.Equal(NotificationStatus.NotSent, n.Status);
        Assert.Null(n.ProviderMessageSid);
    }

    [Fact]
    public void RecordingAcceptanceCapturesProviderSidAndStatus()
    {
        var n = OrderNotification.Create(1, "buyer", NotificationType.OrderPlaced, "+15551230000", "hi");
        n.RecordAccepted("SM123", NotificationStatus.Queued, null, null);
        Assert.Equal("SM123", n.ProviderMessageSid);
        Assert.Equal(NotificationStatus.Queued, n.Status);
    }

    [Fact]
    public void DisposingContentClearsBodyButKeepsTheRecord()
    {
        var n = OrderNotification.Create(1, "buyer", NotificationType.OrderPlaced, "+15551230000", "secret body");
        n.RecordAccepted("SM123", NotificationStatus.Delivered, null, null);

        n.DisposeContent();

        Assert.Null(n.Body);
        Assert.True(n.ContentDisposed);
        // The fact that a message was sent, and what became of it, survives.
        Assert.Equal("SM123", n.ProviderMessageSid);
        Assert.Equal(NotificationStatus.Delivered, n.Status);
    }

    [Theory]
    [InlineData(NotificationStatus.Failed, true)]
    [InlineData(NotificationStatus.Undelivered, true)]
    [InlineData(NotificationStatus.Delivered, false)]
    [InlineData(NotificationStatus.Queued, false)]
    [InlineData(NotificationStatus.Scheduled, false)]
    public void OnlyUndeliverableMessagesAreEligibleForResend(string status, bool expected)
    {
        Assert.Equal(expected, NotificationStatus.IsUndeliverable(status));
    }

    [Theory]
    [InlineData(NotificationStatus.Delivered, true)]
    [InlineData(NotificationStatus.Failed, true)]
    [InlineData(NotificationStatus.Undelivered, true)]
    [InlineData(NotificationStatus.Canceled, true)]
    [InlineData(NotificationStatus.NotSent, true)]
    [InlineData(NotificationStatus.Queued, false)]
    [InlineData(NotificationStatus.Scheduled, false)]
    [InlineData(NotificationStatus.Sending, false)]
    public void TerminalStatusesAreNotRefreshedAgain(string status, bool expected)
    {
        Assert.Equal(expected, NotificationStatus.IsTerminal(status));
    }
}
