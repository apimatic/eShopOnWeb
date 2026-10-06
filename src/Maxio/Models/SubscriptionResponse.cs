namespace Maxio.Models;

/// <summary>
/// Response body for subscription endpoints. Mirrors the Subscription Response schema in the Maxio OpenAPI specification.
/// </summary>
public class SubscriptionResponse
{
    public Subscription Subscription { get; set; } = new();
}
