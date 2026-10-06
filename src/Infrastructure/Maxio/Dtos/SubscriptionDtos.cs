namespace Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;

/// <summary>Request body for POST /subscriptions.json.</summary>
public class CreateSubscriptionRequest
{
    public CreateSubscriptionBody Subscription { get; set; } = new();

    /// <summary>
    /// Optional uniqueness token. When a request with the same token is received within
    /// 60 minutes it is rejected with a 409 Conflict, which lets us retry safely and
    /// prevents duplicate subscriptions from a double-click.
    /// </summary>
    public string? UniquenessToken { get; set; }
}

public class CreateSubscriptionBody
{
    public string? ProductHandle { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public string? Reference { get; set; }
}

/// <summary>Response body for POST /subscriptions.json and GET /subscriptions/{id}.json.</summary>
public class SubscriptionResponse
{
    public MaxioSubscriptionDto Subscription { get; set; } = new();
}

/// <summary>Response body for GET /subscriptions.json and GET /customers/{id}/subscriptions.json (array of wrapped subscriptions).</summary>
public class SubscriptionListResponse
{
    public MaxioSubscriptionDto Subscription { get; set; } = new();
}
