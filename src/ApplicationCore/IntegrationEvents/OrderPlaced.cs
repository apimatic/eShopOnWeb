using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published in-process (best-effort, §2.5) after an order is created — the UC2 "one order placed → one billable unit" hook.</summary>
public class OrderPlaced : INotification
{
    public OrderPlaced(string buyerId)
    {
        BuyerId = buyerId;
    }

    public string BuyerId { get; }
}
