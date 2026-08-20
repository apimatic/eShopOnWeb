namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationSettings
{
    int FollowUpDelayDays { get; }
    string FromNumber { get; }
}
