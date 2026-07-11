using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public class SubscriptionStateChanged : INotification
{
    public int SubscriptionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int MaxioSubscriptionId { get; set; }
    public SubscriptionState OldState { get; set; }
    public SubscriptionState NewState { get; set; }
}
