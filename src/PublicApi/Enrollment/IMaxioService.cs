using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;

namespace Microsoft.eShopWeb.PublicApi.Enrollment;

public interface IMaxioService
{
    Task<JsonObject?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default);
    Task<JsonObject?> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken ct = default);
    Task<JsonObject?> ListFamilyProductsAsync(CancellationToken ct = default);
    Task<JsonObject?> CreateSubscriptionAsync(string productHandle, string customerReference, CancellationToken ct = default);
    Task<JsonObject?> ListSubscriptionsByCustomerReferenceAsync(string reference, CancellationToken ct = default);
}
