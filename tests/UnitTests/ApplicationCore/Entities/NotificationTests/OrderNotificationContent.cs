using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.NotificationTests;

public class OrderNotificationContent
{
    [Fact]
    public void RedactingClearsBodyFromDisplay()
    {
        var notification = new OrderNotification(1, "buyer-1", NotificationKind.OrderPlaced, "Your order was placed.", 1);

        Assert.Equal("Your order was placed.", notification.GetBodyForDisplay());

        notification.MarkContentRedacted();

        Assert.True(notification.ContentRedacted);
        Assert.Null(notification.GetBodyForDisplay());
        Assert.Null(notification.Body);
    }

    [Fact]
    public void ProviderEmptyBodyMarksContentRedacted()
    {
        var notification = new OrderNotification(1, "buyer-1", NotificationKind.OrderPlaced, "Your order was placed.", 1);
        notification.RecordProviderAcceptance("SM123", "delivered");

        notification.SyncFromProvider("delivered", null, null, string.Empty, null);

        Assert.True(notification.ContentRedacted);
        Assert.Null(notification.GetBodyForDisplay());
    }
}
