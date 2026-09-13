using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioSubscriptionService
{
    Task<List<ProductDto>> ListPlansAsync();
    Task<SubscriptionDto> SubscribeAsync(string userReference, string firstName, string lastName, string email, string productHandle);
    Task<List<SubscriptionDto>> GetMySubscriptionsAsync(string userReference);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioApiClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<List<ProductDto>> ListPlansAsync()
    {
        var products = await _client.ListProductsAsync(_settings.ProductFamilyHandle);
        return products ?? new List<ProductDto>();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        string userReference, string firstName, string lastName, string email, string productHandle)
    {
        _logger.LogInformation("Ensuring Maxio customer exists for user {Reference}", userReference);

        var customer = await _client.FindCustomerByReferenceAsync(userReference);
        if (customer == null)
        {
            _logger.LogInformation("Creating Maxio customer for user {Reference}", userReference);
            customer = await _client.CreateCustomerAsync(userReference, firstName, lastName, email);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {Reference}", customer.Id, userReference);
        }
        else
        {
            _logger.LogInformation("Found existing Maxio customer {CustomerId} for user {Reference}", customer.Id, userReference);
        }

        _logger.LogInformation("Creating subscription for customer {CustomerId} to plan {ProductHandle}", customer.Id, productHandle);
        var subscription = await _client.CreateSubscriptionAsync(customer.Id, productHandle);
        _logger.LogInformation("Created subscription {SubscriptionId} in state {State}", subscription.Id, subscription.State);

        return subscription;
    }

    public async Task<List<SubscriptionDto>> GetMySubscriptionsAsync(string userReference)
    {
        var customer = await _client.FindCustomerByReferenceAsync(userReference);
        if (customer == null)
        {
            _logger.LogInformation("No Maxio customer found for user {Reference}", userReference);
            return new List<SubscriptionDto>();
        }

        return await _client.ListCustomerSubscriptionsAsync(customer.Id) ?? new List<SubscriptionDto>();
    }
}
