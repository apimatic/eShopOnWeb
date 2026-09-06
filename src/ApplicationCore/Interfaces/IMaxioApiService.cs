using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class MaxioSubscriptionPlan
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public string Description { get; set; } = "";
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public long ProductPriceInCents { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CreatedAt { get; set; }
    public MaxioSubscriptionProduct? Product { get; set; }
    public MaxioSubscriptionCustomer? Customer { get; set; }
}

public class MaxioSubscriptionProduct
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
}

public class MaxioSubscriptionCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
}

public interface IMaxioApiService
{
    /// <summary>
    /// List all subscription plans in the configured product family
    /// </summary>
    Task<List<MaxioSubscriptionPlan>> ListSubscriptionPlansAsync();

    /// <summary>
    /// Get or create a customer by eShopOnWeb user ID (idempotent)
    /// </summary>
    Task<MaxioCustomer> GetOrCreateCustomerAsync(string userReference, string firstName, string lastName, string email);

    /// <summary>
    /// Create a subscription for a customer on a specific plan
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle);

    /// <summary>
    /// List all subscriptions for a customer
    /// </summary>
    Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId);
}
