using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class MaxioSiteResponse
{
    public required MaxioSite Site { get; init; }
}

internal sealed class MaxioSite
{
    public string Currency { get; init; } = string.Empty;
    public bool Test { get; init; }
}

internal sealed class MaxioProductResponse
{
    public required MaxioProduct Product { get; init; }
}

internal sealed class MaxioProduct
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Handle { get; init; }
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public DateTimeOffset? ArchivedAt { get; init; }
    public bool RequireCreditCard { get; init; }
    public required MaxioProductFamily ProductFamily { get; init; }
}

internal sealed class MaxioProductFamily
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Handle { get; init; } = string.Empty;
}

internal sealed class MaxioCustomerResponse
{
    public required MaxioCustomer Customer { get; init; }
}

internal sealed class MaxioCustomer
{
    public int Id { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Reference { get; init; }
}

internal sealed class MaxioCreateCustomerRequest
{
    public required MaxioCreateCustomer Customer { get; init; }
}

internal sealed class MaxioCreateCustomer
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public required string Reference { get; init; }
}

internal sealed class MaxioSubscriptionResponse
{
    public required MaxioSubscription Subscription { get; init; }
}

internal sealed class MaxioSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public long ProductPriceInCents { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? Reference { get; init; }
    public string Currency { get; init; } = string.Empty;
    public required MaxioCustomer Customer { get; init; }
    public required MaxioProduct Product { get; init; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    public required MaxioCreateSubscription Subscription { get; init; }
}

internal sealed class MaxioCreateSubscription
{
    public required string ProductHandle { get; init; }
    public int CustomerId { get; init; }
    public required string Reference { get; init; }
    public required string PaymentCollectionMethod { get; init; }
}

internal interface IMaxioClient
{
    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken);
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}
