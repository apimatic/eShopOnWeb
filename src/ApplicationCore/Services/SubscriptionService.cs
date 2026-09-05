using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public interface ISubscriptionService
{
    Task<List<MaxioProductDto>> GetSubscriptionPlansAsync();
    Task<MaxioCustomerDto> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName);
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<List<MaxioSubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(IMaxioClient maxioClient, MaxioSettings settings, ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<List<MaxioProductDto>> GetSubscriptionPlansAsync()
    {
        try
        {
            _logger.LogInformation("Fetching subscription plans from product family {Handle}", _settings.ProductFamilyHandle);

            var queryParams = new Dictionary<string, string>
            {
                { "product_family_handle", _settings.ProductFamilyHandle }
            };

            var response = await _maxioClient.GetAsync<List<MaxioProductResponseDto>>("/products.json", queryParams);

            if (response == null)
            {
                return new List<MaxioProductDto>();
            }

            return response
                .Where(r => r.Product != null)
                .Select(r => r.Product!)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans");
            throw;
        }
    }

    public async Task<MaxioCustomerDto> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        try
        {
            _logger.LogInformation("Getting or creating customer for user {UserId}", userId);

            var lookupQueryParams = new Dictionary<string, string>
            {
                { "reference", userId }
            };

            var existingCustomer = await _maxioClient.GetAsync<List<MaxioCustomerResponseDto>>("/customers/lookup.json", lookupQueryParams);

            if (existingCustomer?.Count > 0 && existingCustomer[0]?.Customer != null)
            {
                var customer = existingCustomer[0]!.Customer;
                _logger.LogInformation("Found existing customer {CustomerId} for user {UserId}", customer.Id, userId);
                return customer;
            }

            _logger.LogInformation("Creating new customer for user {UserId}", userId);

            var createRequest = new CreateCustomerRequestDto
            {
                Customer = new MaxioCustomerDto
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = userId,
                    Country = "US"
                }
            };

            var createResponse = await _maxioClient.PostAsync<MaxioCustomerResponseDto>("/customers.json", createRequest);

            if (createResponse?.Customer == null)
            {
                throw new InvalidOperationException("Failed to create customer: no customer in response");
            }

            _logger.LogInformation("Created customer {CustomerId} for user {UserId}", createResponse.Customer.Id, userId);

            return createResponse.Customer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating customer for user {UserId}", userId);
            throw;
        }
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            _logger.LogInformation("Creating subscription for customer {CustomerId} with product {ProductHandle}", customerId, productHandle);

            var createRequest = new CreateSubscriptionRequestDto
            {
                Subscription = new CreateSubscriptionDto
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle,
                    PaymentCollectionMethod = "remittance"
                }
            };

            var response = await _maxioClient.PostAsync<MaxioSubscriptionResponseDto>("/subscriptions.json", createRequest);

            if (response?.Subscription == null)
            {
                throw new InvalidOperationException("Failed to create subscription: no subscription in response");
            }

            _logger.LogInformation("Created subscription {SubscriptionId} for customer {CustomerId}", response.Subscription.Id, customerId);

            return response.Subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for customer {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<List<MaxioSubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            _logger.LogInformation("Fetching subscriptions for customer {CustomerId}", customerId);

            var queryParams = new Dictionary<string, string>
            {
                { "customer_id", customerId.ToString() }
            };

            var response = await _maxioClient.GetAsync<List<MaxioSubscriptionResponseDto>>("/subscriptions.json", queryParams);

            if (response == null)
            {
                return new List<MaxioSubscriptionDto>();
            }

            return response
                .Where(r => r.Subscription != null)
                .Select(r => r.Subscription!)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for customer {CustomerId}", customerId);
            throw;
        }
    }
}
