namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioClient
{
    Task<MaxioCustomer?> CreateOrGetCustomerAsync(string email, string firstName, string lastName);
    Task<MaxioCustomer?> LookupCustomerByEmailAsync(string email);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<IEnumerable<MaxioProduct>> GetProductsAsync(string productFamilyHandle);
    Task<MaxioProduct?> GetProductByHandleAsync(string productHandle);
    Task<IEnumerable<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId);
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PriceInCents { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? ActivatedAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public decimal BalanceInCents { get; set; }
}
