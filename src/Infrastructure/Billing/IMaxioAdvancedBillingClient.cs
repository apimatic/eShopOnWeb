using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Typed client for the Maxio Advanced Billing operations used by eShopOnWeb.
/// Paths, query params, and payloads match maxio-spec/openapi.yaml.
/// </summary>
public interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<MaxioProductSnapshot>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    Task<MaxioCustomerSnapshot?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<MaxioCustomerSnapshot> CreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        string reference,
        CancellationToken cancellationToken = default);

    Task<MaxioSubscriptionSnapshot> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string? reference,
        string? paymentCollectionMethod,
        CancellationToken cancellationToken = default);

    Task<MaxioSubscriptionSnapshot?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscriptionSnapshot>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);
}

public sealed class MaxioProductSnapshot
{
    public int Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string? ProductFamilyHandle { get; init; }
}

public sealed class MaxioCustomerSnapshot
{
    public int Id { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Reference { get; init; }
}

public sealed class MaxioSubscriptionSnapshot
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public long ProductPriceInCents { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public string? Reference { get; init; }
    public string? ProductHandle { get; init; }
    public string? ProductName { get; init; }
}
