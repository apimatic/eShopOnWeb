using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port over the Maxio Advanced Billing REST API used by subscription enrollment.
/// </summary>
public interface IMaxioBillingClient
{
    Task<IReadOnlyList<BillingProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<BillingCustomer> CreateCustomerAsync(
        BillingCustomerDraft customer,
        string uniquenessToken,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    Task<BillingSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<BillingSubscription> CreateSubscriptionAsync(
        BillingSubscriptionDraft subscription,
        string uniquenessToken,
        CancellationToken cancellationToken = default);
}

public class BillingProduct
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class BillingCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string Email { get; set; } = string.Empty;
}

public class BillingCustomerDraft
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public class BillingSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Reference { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
}

public class BillingSubscriptionDraft
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string Reference { get; set; } = string.Empty;
}
