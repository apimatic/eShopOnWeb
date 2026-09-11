using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore;

public class MaxioPlan
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal AmountInCents { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextBillingAt { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(string productFamilyHandle);
    Task<MaxioSubscription> SubscribeAsync(string userReference, string productHandle);
    Task<IReadOnlyList<MaxioSubscription>> GetMySubscriptionsAsync(string userReference);
}
