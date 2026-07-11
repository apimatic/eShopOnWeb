using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public class SubscriptionPlanChanged : INotification
{
    public int SubscriptionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int MaxioSubscriptionId { get; set; }
    public int OldProductId { get; set; }
    public string OldProductHandle { get; set; } = string.Empty;
    public int NewProductId { get; set; }
    public string NewProductHandle { get; set; } = string.Empty;
}
