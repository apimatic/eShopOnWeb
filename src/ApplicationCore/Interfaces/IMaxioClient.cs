using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioClient
{
    Task<MaxioCustomer> CreateCustomerAsync(string email, string firstName, string lastName);
    Task<IEnumerable<SubscriptionPlan>> GetSubscriptionPlansAsync();
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<IEnumerable<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId);
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}

public class SubscriptionPlan
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int? ProductFamilyId { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string CurrentPeriodStartsAt { get; set; } = string.Empty;
    public string CurrentPeriodEndsAt { get; set; } = string.Empty;
    public decimal CurrentPrice { get; set; }
    public string NextBillingAt { get; set; } = string.Empty;
}
