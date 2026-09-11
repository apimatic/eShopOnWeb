using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioService : IMaxioService
{
    private readonly MaxioClient _client;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(MaxioClient client, ILogger<MaxioService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<List<Models.Product>> ListPlansAsync()
    {
        return await _client.ListProductsForFamilyAsync();
    }

    public async Task<Models.Subscription> SubscribeAsync(string userReference, string productHandle, string email, string firstName, string lastName)
    {
        var customer = await _client.FindOrCreateCustomerAsync(firstName, lastName, email, userReference);

        // Idempotency: check if customer already has an active/pending subscription for this product
        var existingSubs = await _client.ListSubscriptionsByCustomerIdAsync(customer.Id);
        var existing = existingSubs.Find(s =>
            s.Product?.Handle == productHandle &&
            s.State is "active" or "pending" or "trialing" or "assessing");
        if (existing != null)
        {
            _logger.LogInformation("Customer {CustomerId} already has subscription {SubId} for {Handle}", customer.Id, existing.Id, productHandle);
            return existing;
        }

        return await _client.CreateSubscriptionAsync(customer.Id, productHandle);
    }

    public async Task<List<Models.Subscription>> GetMySubscriptionsAsync(string userReference)
    {
        var customer = await _client.FindCustomerByReferenceAsync(userReference);
        if (customer == null)
        {
            _logger.LogInformation("No Maxio customer found for reference {Reference}", userReference);
            return new List<Models.Subscription>();
        }

        return await _client.ListSubscriptionsByCustomerIdAsync(customer.Id);
    }
}
