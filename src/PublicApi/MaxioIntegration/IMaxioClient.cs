using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.MaxioIntegration;

public interface IMaxioClient
{
    Task<MaxioCustomer?> GetOrCreateCustomerAsync(string email, string firstName, string lastName, string externalId);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string? productPricePointHandle = null);
    Task<List<MaxioProduct>> ListProductsAsync();
    Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId);
    Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId);
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public int ProductFamilyId { get; set; }
    public string ProductFamilyName { get; set; } = string.Empty;
}

public class MaxioProductPrice
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PriceInCents { get; set; }
    public int IntervalInDays { get; set; }
    public string Interval { get; set; } = string.Empty;
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal CurrentPriceInCents { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? NextBillingAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
}
