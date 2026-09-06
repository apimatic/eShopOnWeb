using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<CustomerData?> GetOrCreateCustomerAsync(ApplicationUser user, CancellationToken cancellationToken = default);
    Task<List<ProductData>> ListPlansAsync(CancellationToken cancellationToken = default);
    Task<SubscriptionData?> CreateSubscriptionAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken = default);
    Task<List<SubscriptionData>> GetUserSubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken = default);
}

public class MaxioService : IMaxioService
{
    private readonly MaxioHttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly AppIdentityDbContext _identityContext;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(
        MaxioHttpClient httpClient,
        IOptions<MaxioConfiguration> options,
        AppIdentityDbContext identityContext,
        ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _config = options.Value;
        _identityContext = identityContext;
        _logger = logger;
    }

    public async Task<CustomerData?> GetOrCreateCustomerAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var customerId = await GetStoredCustomerIdAsync(user.Id, cancellationToken);
        if (customerId.HasValue)
        {
            _logger.LogInformation("Found existing Maxio customer {CustomerId} for user {UserId}", customerId, user.Id);
            return new CustomerData { id = customerId.Value };
        }

        _logger.LogInformation("Creating new Maxio customer for user {UserId}", user.Id);
        var request = new MaxioCustomerRequest
        {
            customer = new Customer
            {
                first_name = user.Id.Split('@')[0] ?? "Customer",
                last_name = "",
                email = user.Email ?? "",
                reference = user.Id
            }
        };

        var response = await _httpClient.PostAsync<MaxioCustomerRequest, MaxioCustomerResponse>(
            "/customers.json",
            request,
            cancellationToken);

        if (response?.customer == null)
        {
            _logger.LogError("Failed to create Maxio customer for user {UserId}", user.Id);
            return null;
        }

        await StoreCustomerIdAsync(user.Id, response.customer.id, cancellationToken);
        _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}", response.customer.id, user.Id);

        return response.customer;
    }

    public async Task<List<ProductData>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching products from Maxio");
        var response = await _httpClient.GetAsync<MaxioProductsResponse>(
            "/products.json",
            cancellationToken);

        if (response?.products == null)
        {
            _logger.LogError("Failed to fetch products from Maxio");
            return new List<ProductData>();
        }

        return response.products;
    }

    public async Task<SubscriptionData?> CreateSubscriptionAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken = default)
    {
        var customer = await GetOrCreateCustomerAsync(user, cancellationToken);
        if (customer == null)
        {
            _logger.LogError("Failed to ensure customer exists for subscription creation");
            return null;
        }

        _logger.LogInformation("Creating subscription for user {UserId} with plan {Plan}", user.Id, planHandle);
        var request = new MaxioCreateSubscriptionRequest
        {
            subscription = new CreateSubscriptionData
            {
                product_handle = planHandle,
                customer_id = customer.id,
                payment_collection_method = "remittance"
            }
        };

        var response = await _httpClient.PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionResponse>(
            "/subscriptions.json",
            request,
            cancellationToken);

        if (response?.subscription == null)
        {
            _logger.LogError("Failed to create subscription for user {UserId}", user.Id);
            return null;
        }

        _logger.LogInformation("Created subscription {SubscriptionId} for user {UserId}", response.subscription.id, user.Id);
        return response.subscription;
    }

    public async Task<List<SubscriptionData>> GetUserSubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var customer = await GetOrCreateCustomerAsync(user, cancellationToken);
        if (customer == null)
        {
            return new List<SubscriptionData>();
        }

        _logger.LogInformation("Fetching subscriptions for customer {CustomerId}", customer.id);
        var response = await _httpClient.GetAsync<MaxioSubscriptionsListResponse>(
            $"/customers/{customer.id}/subscriptions.json",
            cancellationToken);

        if (response?.subscriptions == null)
        {
            _logger.LogError("Failed to fetch subscriptions for customer {CustomerId}", customer.id);
            return new List<SubscriptionData>();
        }

        return response.subscriptions;
    }

    private async Task<int?> GetStoredCustomerIdAsync(string userId, CancellationToken cancellationToken)
    {
        var mapping = _identityContext.UserMaxioCustomerMappings
            .FirstOrDefault(m => m.UserId == userId);
        return mapping?.MaxioCustomerId;
    }

    private async Task StoreCustomerIdAsync(string userId, int customerId, CancellationToken cancellationToken)
    {
        var existingMapping = _identityContext.UserMaxioCustomerMappings
            .FirstOrDefault(m => m.UserId == userId);

        if (existingMapping != null)
        {
            existingMapping.MaxioCustomerId = customerId;
        }
        else
        {
            var mapping = new UserMaxioCustomerMapping
            {
                UserId = userId,
                MaxioCustomerId = customerId,
                CreatedAt = DateTime.UtcNow
            };
            _identityContext.UserMaxioCustomerMappings.Add(mapping);
        }

        await _identityContext.SaveChangesAsync(cancellationToken);
    }
}
