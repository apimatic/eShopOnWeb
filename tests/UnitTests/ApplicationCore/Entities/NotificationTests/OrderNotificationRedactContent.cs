using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.NotificationTests;

public class OrderNotificationRedactContent
{
    [Fact]
    public void RedactContentClearsBodyAndSetsFlag()
    {
        var notification = new OrderNotification(
            orderId: 1,
            buyerId: "buyer",
            kind: OrderNotificationKind.OrderPlaced,
            body: "Your eShop order #1 has been placed.",
            contactNumberId: 1,
            destination: "+15555550100");

        notification.RedactContent();

        Assert.True(notification.ContentRedacted);
        Assert.Equal(string.Empty, notification.Body);
    }
}
