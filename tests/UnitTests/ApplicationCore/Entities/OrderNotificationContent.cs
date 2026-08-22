using Microsoft.eShopWeb.ApplicationCore.Entities;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities;

public class OrderNotificationContent
{
    [Fact]
    public void RedactClearsBodyAndKeepsProviderOutcome()
    {
        var notification = new OrderNotification(1, "buyer", OrderNotificationKind.OrderPlaced, "stored-destination", "hello");
        notification.RecordProviderResult("SM1", "delivered", null, null, "hello");

        notification.RedactContent();

        Assert.True(notification.ContentRedacted);
        Assert.Null(notification.Body);
        Assert.Equal("SM1", notification.ProviderSid);
        Assert.Equal("delivered", notification.ProviderStatus);
    }

    [Fact]
    public void EmptyProviderBodyIsTreatedAsRedacted()
    {
        var notification = new OrderNotification(1, "buyer", OrderNotificationKind.OrderPlaced, "stored-destination", "hello");
        notification.RecordProviderResult("SM1", "delivered", null, null, "hello");

        notification.RecordProviderResult("SM1", "delivered", null, null, "");

        Assert.True(notification.ContentRedacted);
        Assert.Null(notification.Body);
        Assert.Equal("delivered", notification.ProviderStatus);
    }
}
