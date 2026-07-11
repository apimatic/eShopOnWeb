using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public class SubscriptionActivated : INotification
{
    public int SubscriptionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int MaxioSubscriptionId { get; set; }
    public int ProductId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
}
