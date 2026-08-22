using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.NotificationTests;

public class OrderNotificationTests
{
    [Fact]
    public void RecordsProviderSidAndStatus()
    {
        var notification = new OrderNotification(1, "buyer", 9, "+15555550100", NotificationPurpose.OrderPlaced, "placed");
        notification.RecordProviderAccepted("SM12345678901234567890123456789012", "queued", null, null);

        Assert.Equal("SM12345678901234567890123456789012", notification.ProviderMessageSid);
        Assert.Equal("queued", notification.ProviderStatus);
    }

    [Fact]
    public void RedactClearsBodyAndKeepsProviderSid()
    {
        var notification = new OrderNotification(1, "buyer", 9, "+15555550100", NotificationPurpose.OrderPlaced, "secret text");
        notification.RecordProviderAccepted("SM12345678901234567890123456789012", "delivered", null, null);
        notification.RedactContent();

        Assert.True(notification.ContentRedacted);
        Assert.Null(notification.Body);
        Assert.Equal("SM12345678901234567890123456789012", notification.ProviderMessageSid);
        Assert.Equal("delivered", notification.ProviderStatus);
    }

    [Fact]
    public void SendFailureDoesNotClearIdentity()
    {
        var notification = new OrderNotification(1, "buyer", 9, "+15555550100", NotificationPurpose.OrderPlaced, "placed");
        notification.RecordSendFailure("Timeout");

        Assert.Equal("send_failed", notification.ProviderStatus);
        Assert.Equal("Timeout", notification.SendFailureReason);
        Assert.Null(notification.ProviderMessageSid);
    }
}
