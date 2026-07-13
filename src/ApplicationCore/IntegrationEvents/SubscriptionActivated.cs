using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published after a customer is successfully enrolled in a plan (UC1). Delivery is best-effort,
/// in-process only (§2.5) — there is no durable outbox.
/// </summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string customerReference, long subscriptionId, string productHandle, string productName, long priceInCents)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
    }

    public string CustomerReference { get; }
    public long SubscriptionId { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public long PriceInCents { get; }
}
