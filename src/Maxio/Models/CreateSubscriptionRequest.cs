namespace Maxio.Models;

/// <summary>
/// Request body for POST /subscriptions.json. Mirrors the Create Subscription Request schema in the Maxio OpenAPI specification.
/// </summary>
public class CreateSubscriptionRequest
{
    public CreateSubscription Subscription { get; set; } = new();
}
