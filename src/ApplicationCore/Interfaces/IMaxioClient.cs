using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class MaxioProduct
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public decimal PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string? State { get; set; }
    public string? NextBillingAt { get; set; }
    public decimal CurrentPriceInCents { get; set; }
}

public interface IMaxioClient
{
    Task<List<MaxioProduct>> GetProductsForFamilyAsync(string productFamilyHandle);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId);
    Task<int> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName);
    Task<int> FindCustomerByReferenceAsync(string reference);
}
