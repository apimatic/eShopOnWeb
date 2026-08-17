using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.NotificationTests;

public class NotificationLifecycle
{
    private static Notification NewNotification()
        => new("buyer@example.com", 1, NotificationKind.OrderPlaced, "+15005550006", "eShop: your order was placed.");

    [Fact]
    public void MarkSentRecordsProviderIdentifierAndOutcome()
    {
        var n = NewNotification();
        n.MarkSent("SM123", "queued", null);

        Assert.Equal(NotificationState.Sent, n.State);
        Assert.Equal("SM123", n.ProviderMessageSid);
        Assert.Equal("queued", n.ProviderStatus);
        Assert.NotNull(n.SentAt);
    }

    [Fact]
    public void RedactContentDropsBodyButKeepsTheRecordAndOutcome()
    {
        var n = NewNotification();
        n.MarkSent("SM123", "delivered", null);

        n.RedactContent();

        Assert.True(n.ContentRedacted);
        Assert.Null(n.Body);
        // The fact it was sent, and what became of it, survive.
        Assert.Equal("SM123", n.ProviderMessageSid);
        Assert.Equal("delivered", n.ProviderStatus);
        Assert.Equal(NotificationState.Sent, n.State);
    }

    [Fact]
    public void MarkAsResendCarriesIdempotencyKeyAndOriginalLink()
    {
        var n = NewNotification();
        n.MarkAsResendOf(42, "key-abc");

        Assert.Equal(42, n.ResendOfNotificationId);
        Assert.Equal("key-abc", n.IdempotencyKey);
    }

    [Fact]
    public void MarkCancelledReflectsAScheduledFollowUpBeingCalledOff()
    {
        var n = NewNotification();
        n.MarkScheduled("SM999", "scheduled", System.DateTimeOffset.UtcNow.AddDays(3));

        n.MarkCancelled("canceled");

        Assert.Equal(NotificationState.Cancelled, n.State);
        Assert.Equal("canceled", n.ProviderStatus);
        Assert.Equal("SM999", n.ProviderMessageSid);
    }
}
