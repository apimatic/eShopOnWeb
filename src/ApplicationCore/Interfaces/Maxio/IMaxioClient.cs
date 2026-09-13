using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Maxio;

public interface IMaxioClient
{
    Task<MaxioProduct[]> GetProductsByFamilyAsync(string productFamilyHandle, CancellationToken ct = default);
    Task<MaxioProduct> GetProductByHandleAsync(string handle, CancellationToken ct = default);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken ct = default);
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken ct = default);
    Task<MaxioSubscription[]> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);
    Task<MaxioSubscription> ReadSubscriptionAsync(int subscriptionId, CancellationToken ct = default);
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public string? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
    public int ProductPricePointId { get; set; }
    public string ProductPricePointName { get; set; } = string.Empty;
}

public class MaxioProductFamily
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long BalanceInCents { get; set; }
    public long TotalRevenueInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public int ProductVersionNumber { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? TrialStartedAt { get; set; }
    public string? TrialEndedAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? ExpiresAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string? CanceledAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public string Currency { get; set; } = "USD";
    public string? CurrentBillingAmountInCents { get; set; }
    public int ProductPricePointId { get; set; }
    public string? Reference { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

public class MaxioCreateCustomerRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class MaxioCreateSubscriptionRequest
{
    public string? ProductHandle { get; set; }
    public int? ProductId { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public MaxioCustomerAttributes? CustomerAttributes { get; set; }
    public int? ProductPricePointId { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public string? Reference { get; set; }
}

public class MaxioCustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}
