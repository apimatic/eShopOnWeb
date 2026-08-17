using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.NotificationTests;

public class SmsStatusMapperTests
{
    [Theory]
    [InlineData("queued", NotificationStatus.Queued)]
    [InlineData("accepted", NotificationStatus.Accepted)]
    [InlineData("scheduled", NotificationStatus.Scheduled)]
    [InlineData("sent", NotificationStatus.Sent)]
    [InlineData("delivered", NotificationStatus.Delivered)]
    [InlineData("undelivered", NotificationStatus.Undelivered)]
    [InlineData("failed", NotificationStatus.Failed)]
    [InlineData("canceled", NotificationStatus.Canceled)]
    [InlineData("cancelled", NotificationStatus.Canceled)]
    [InlineData("DELIVERED", NotificationStatus.Delivered)]
    [InlineData("something-new", NotificationStatus.Unknown)]
    [InlineData(null, NotificationStatus.Unknown)]
    public void MapsProviderStatusStrings(string? providerStatus, NotificationStatus expected)
    {
        Assert.Equal(expected, SmsStatusMapper.Map(providerStatus));
    }

    [Theory]
    [InlineData(NotificationStatus.Delivered, true)]
    [InlineData(NotificationStatus.Undelivered, true)]
    [InlineData(NotificationStatus.Failed, true)]
    [InlineData(NotificationStatus.Canceled, true)]
    [InlineData(NotificationStatus.SendError, true)]
    [InlineData(NotificationStatus.Queued, false)]
    [InlineData(NotificationStatus.Scheduled, false)]
    [InlineData(NotificationStatus.Sent, false)]
    public void KnowsTerminalStatuses(NotificationStatus status, bool terminal)
    {
        Assert.Equal(terminal, SmsStatusMapper.IsTerminal(status));
    }
}
