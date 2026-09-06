using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioService
{
    Task<MaxioProduct[]> GetPlansAsync(CancellationToken cancellationToken = default);
    Task<MaxioCustomer> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName, CancellationToken cancellationToken = default);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default);
    Task<MaxioSubscription[]> GetSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public string Description { get; set; } = "";
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "";
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Reference { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string State { get; set; } = "";
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
}
