using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioService
{
    Task<JsonNode?> GetProductsAsync(string familyHandle, CancellationToken ct = default);
    Task<JsonNode?> GetCustomerByReferenceAsync(string reference, CancellationToken ct = default);
    Task<JsonNode?> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken ct = default);
    Task<JsonNode?> GetCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);
    Task<JsonNode?> CreateSubscriptionAsync(string productHandle, string customerReference, CancellationToken ct = default);
}
