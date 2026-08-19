using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Typed access to the Maxio Advanced Billing operations this integration uses.
/// Paths, query params, and bodies match <c>maxio-spec/openapi.yaml</c>.
/// </summary>
public interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<MaxioProductDto>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    Task<MaxioProductDto?> ReadProductByHandleAsync(
        string productHandle,
        CancellationToken cancellationToken = default);

    Task<MaxioCustomerDto?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<MaxioCustomerDto> CreateCustomerAsync(
        MaxioCreateCustomerDto customer,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    Task<MaxioSubscriptionDto?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(
        MaxioCreateSubscriptionDto subscription,
        CancellationToken cancellationToken = default);
}

public sealed class MaxioProductDto
{
    public int Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string? ProductFamilyHandle { get; init; }
    public bool IsArchived { get; init; }
}

public sealed class MaxioCustomerDto
{
    public int Id { get; init; }
    public string? Email { get; init; }
    public string? Reference { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}

public sealed class MaxioCreateCustomerDto
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
}

public sealed class MaxioSubscriptionDto
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public long ProductPriceInCents { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? Reference { get; init; }
    public string? ProductHandle { get; init; }
    public string? ProductName { get; init; }
    public string? ProductFamilyHandle { get; init; }
}

public sealed class MaxioCreateSubscriptionDto
{
    public string ProductHandle { get; init; } = string.Empty;
    public int? CustomerId { get; init; }
    public string? CustomerReference { get; init; }
    public string? Reference { get; init; }
    public string? PaymentCollectionMethod { get; init; }
}
