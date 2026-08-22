using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.NotificationTests;

public class OrderNotificationTests
{
    [Fact]
    public void RedactingContentClearsBodyButKeepsProviderIdentity()
    {
        var notification = new OrderNotification(1, "buyer", 4, "+15555550100", NotificationKind.OrderPlaced, "hello");
        notification.RecordProviderResult("SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "delivered", null, null);

        notification.MarkContentRedacted();

        Assert.True(notification.ContentRedacted);
        Assert.Null(notification.BodyForDisplay);
        Assert.Equal("SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", notification.ProviderMessageSid);
        Assert.Equal("delivered", notification.ProviderStatus);
    }

    [Fact]
    public void FailedAndUndeliveredAreTreatedAsNotReached()
    {
        var failed = new OrderNotification(1, "buyer", 4, "+15555550100", NotificationKind.OrderPlaced, "hello");
        failed.RecordProviderResult("SM1", "failed", 30003, "Unreachable");
        var undelivered = new OrderNotification(1, "buyer", 4, "+15555550100", NotificationKind.OrderPlaced, "hello");
        undelivered.RecordProviderResult("SM2", "undelivered", 30005, "Unknown destination");

        Assert.True(failed.DidNotReachShopper);
        Assert.True(undelivered.DidNotReachShopper);
    }

    [Fact]
    public void ScheduledQueuedAndAcceptedArePendingWithProvider()
    {
        var scheduled = new OrderNotification(1, "buyer", 4, "+15555550100", NotificationKind.DeliveryFollowUp, "how was it?", scheduledFor: System.DateTimeOffset.UtcNow);
        scheduled.RecordProviderResult("SM3", "scheduled", null, null);

        Assert.True(scheduled.IsPendingWithProvider);
    }

    [Fact]
    public void ContactNumberBelongsOnlyToItsBuyer()
    {
        var number = new ContactNumber("demouser@microsoft.com", "+15555550100");

        Assert.True(number.BelongsTo("demouser@microsoft.com"));
        Assert.False(number.BelongsTo("admin@microsoft.com"));
    }
}
