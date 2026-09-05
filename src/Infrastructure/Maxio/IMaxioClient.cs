using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public interface IMaxioClient
{
    Task<List<ProductResponse>> ListProductsAsync(string familyHandle);
    Task<SubscriptionResponse> CreateSubscriptionAsync(SubscriptionCreateRequest request);
    Task<List<SubscriptionResponse>> ListSubscriptionsByCustomerIdAsync(int customerId);
    Task<CustomerResponse?> GetOrCreateCustomerAsync(string customerReference, string firstName, string lastName, string email);
}

public class CustomerResponse
{
    public Customer Customer { get; set; } = new();
}
