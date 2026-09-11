using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioBillingService
{
    Task<string> GetCustomerReferenceAsync(string reference);
    Task<string> CreateCustomerAsync(string reference, string email, string firstName, string lastName);
    Task<string> CreateSubscriptionAsync(string customerReference, string productHandle);
    Task<List<SubscriptionInfo>> ListSubscriptionsAsync(string customerReference);
    Task<List<ProductInfo>> ListProductsAsync();
}

public class SubscriptionInfo
{
    public int Id { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string NextBillingAt { get; set; } = string.Empty;
    public decimal PriceInCents { get; set; }
}

public class ProductInfo
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PriceInCents { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
}
