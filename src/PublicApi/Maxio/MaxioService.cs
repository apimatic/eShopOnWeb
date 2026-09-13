using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioService
{
    Task<List<MaxioProduct>> GetAvailablePlansAsync(string? familyHandle = null);
    Task<MaxioSubscription> SubscribeAsync(string userEmail, string productHandle);
    Task<List<MaxioSubscription>> GetUserSubscriptionsAsync(string userEmail);
}

public class MaxioService : IMaxioService
{
    private readonly MaxioHttpClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(MaxioHttpClient client, Microsoft.Extensions.Options.IOptions<MaxioSettings> settings, ILogger<MaxioService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<List<MaxioProduct>> GetAvailablePlansAsync(string? familyHandle = null)
    {
        var handle = familyHandle ?? _settings.ProductFamilyHandle;
        return await _client.ListProductsByFamilyAsync(handle);
    }

    public async Task<MaxioSubscription> SubscribeAsync(string userEmail, string productHandle)
    {
        var namePart = userEmail.Split('@')[0];
        var firstName = namePart.Length > 20 ? namePart.Substring(0, 20) : namePart;
        var lastName = "Subscriber";

        var customer = await _client.GetOrCreateCustomerAsync(userEmail, firstName, lastName, userEmail);

        var existingSubscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id);
        var activeForProduct = existingSubscriptions.Find(s =>
            s.Product?.Handle == productHandle &&
            (s.State == "active" || s.State == "trialing" || s.State == "past_due" || s.State == "on_hold"));

        if (activeForProduct != null)
        {
            _logger.LogInformation(
                "User {Email} already has active subscription {SubscriptionId} for product {ProductHandle}",
                userEmail, activeForProduct.Id, productHandle);
            return activeForProduct;
        }

        return await _client.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customer.Id
            }
        });
    }

    public async Task<List<MaxioSubscription>> GetUserSubscriptionsAsync(string userEmail)
    {
        var customer = await _client.FindCustomerByReferenceAsync(userEmail);
        if (customer == null)
        {
            _logger.LogInformation("No Maxio customer found for {Email}", userEmail);
            return new List<MaxioSubscription>();
        }

        return await _client.ListCustomerSubscriptionsAsync(customer.Id);
    }
}
