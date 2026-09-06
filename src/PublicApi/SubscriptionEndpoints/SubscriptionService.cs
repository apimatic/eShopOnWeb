using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionService
{
    private readonly MaxioApiClient _maxioClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(MaxioApiClient maxioClient, IOptions<MaxioConfiguration> options, ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _config = options.Value;
        _logger = logger;
    }

    public async Task<(SubscriptionDto? subscription, string? error)> CreateSubscriptionAsync(string username, string email, string productHandle)
    {
        try
        {
            // If email doesn't contain '@', construct a valid email
            if (!email.Contains("@"))
            {
                email = $"{email}@eshop.local";
            }

            var customer = await GetOrCreateCustomerAsync(username, email);
            if (customer == null)
            {
                return (null, "Failed to create or retrieve customer");
            }

            var subscription = await CreateSubscriptionInMaxioAsync(customer.Id, productHandle);
            return (subscription, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error creating subscription for user {username}");
            return (null, $"Failed to create subscription: {ex.Message}");
        }
    }

    public async Task<List<SubscriptionDto>?> GetSubscriptionsAsync(string username, string email)
    {
        try
        {
            var customer = await GetOrCreateCustomerAsync(username, email);
            if (customer == null)
            {
                return new List<SubscriptionDto>();
            }

            return await GetCustomerSubscriptionsAsync(customer.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving subscriptions for user {username}");
            return null;
        }
    }

    private async Task<CustomerResponseDto?> GetOrCreateCustomerAsync(string username, string email)
    {
        try
        {
            var lookupResponse = await _maxioClient.GetAsync<CustomerLookupResponse>($"/customers/lookup.json?reference={Uri.EscapeDataString(username)}");
            if (lookupResponse?.Customer != null)
            {
                _logger.LogInformation($"Found existing customer for reference {username}");
                return lookupResponse.Customer;
            }

            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomerRequestDto
                {
                    FirstName = username.Split('@')[0] ?? "Customer",
                    LastName = "Subscriber",
                    Email = email?.Trim(),
                    Reference = username
                }
            };

            var createResponse = await _maxioClient.PostAsync<CustomerCreateResponse>("/customers.json", createRequest);
            if (createResponse?.Customer != null)
            {
                _logger.LogInformation($"Created new customer for reference {username}");
                return createResponse.Customer;
            }

            _logger.LogWarning($"Failed to create customer for reference {username}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error managing customer for reference {username}");
            throw;
        }
    }

    private async Task<SubscriptionDto?> CreateSubscriptionInMaxioAsync(int customerId, string productHandle)
    {
        try
        {
            var request = new MaxioCreateSubscriptionRequest
            {
                Subscription = new CreateSubscriptionRequestDto
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle,
                    PaymentCollectionMethod = "remittance"
                }
            };

            var response = await _maxioClient.PostAsync<SubscriptionCreateResponse>("/subscriptions.json", request);
            if (response?.Subscription != null)
            {
                _logger.LogInformation($"Created subscription {response.Subscription.Id} for customer {customerId}");
                return response.Subscription;
            }

            _logger.LogWarning($"Failed to create subscription for customer {customerId}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error creating subscription for customer {customerId}");
            throw;
        }
    }

    private async Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var response = await _maxioClient.GetAsync<SubscriptionsListResponse>($"/subscriptions.json?customer_id={customerId}");
            return response?.Subscriptions ?? new List<SubscriptionDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving subscriptions for customer {customerId}");
            throw;
        }
    }
}

