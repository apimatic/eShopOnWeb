using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Gateway to Maxio Advanced Billing (Billing API). Maxio is the system of record.
/// </summary>
public interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken);

    Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);

    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest request, CancellationToken cancellationToken);
}

public class MaxioProduct
{
    public long Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public bool Archived { get; init; }
}

public class MaxioCustomer
{
    public long Id { get; init; }
    public string? Reference { get; init; }
    public string Email { get; init; } = string.Empty;
}

public class CreateMaxioCustomerRequest
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
}

public class MaxioSubscription
{
    public long Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string? Reference { get; init; }
    public long ProductPriceInCents { get; init; }
    public System.DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
}

public class CreateMaxioSubscriptionRequest
{
    public long CustomerId { get; init; }
    public string ProductHandle { get; init; } = string.Empty;
    public string? Reference { get; init; }
    public string UniquenessToken { get; init; } = string.Empty;
    public string PaymentCollectionMethod { get; init; } = "remittance";
}
