using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

// These transport shapes intentionally model only fields used from the supplied Maxio OpenAPI schemas.
public sealed class MaxioProductResponse
{
    public MaxioProduct Product { get; init; } = new();
}

public sealed class MaxioProduct
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Handle { get; init; }
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public DateTimeOffset? ArchivedAt { get; init; }
}

public sealed class MaxioCustomerResponse
{
    public MaxioCustomer Customer { get; init; } = new();
}

public sealed class MaxioCustomer
{
    public int Id { get; init; }
    public string? Reference { get; init; }
}

public sealed class MaxioSubscriptionResponse
{
    public MaxioSubscription Subscription { get; init; } = new();
}

public sealed class MaxioSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public long ProductPriceInCents { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public MaxioProduct Product { get; init; } = new();
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string body)
        : base($"Maxio returned {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        ResponseBody = body;
    }

    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }
}
