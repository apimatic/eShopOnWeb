using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options, ILogger<SubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var productFamilyId = _options.ProductFamilyHandle ?? "3023074";
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: productFamilyId,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 20,
                ct: ct);

            var plans = new List<SubscriptionPlanDto>();
            foreach (var response in products)
            {
                var product = response.Product;
                if (product != null)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id ?? 0,
                        Name = product.Name ?? "",
                        Handle = product.Handle ?? "",
                        Description = product.Description,
                        PriceInCents = product.PriceInCents ?? 0,
                        Interval = product.Interval ?? 0,
                        IntervalUnit = product.IntervalUnit?.Value ?? "month"
                    });
                }
            }

            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Failed to list subscription plans: HTTP {(int)ex.Error.StatusCode}");
            throw new SubscriptionException("Failed to retrieve subscription plans", (int)ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError($"Failed to parse subscription plans response: {ex.Message}");
            throw new SubscriptionException("The provider returned an invalid response.", innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError($"Connection failed while fetching plans: {ex.Message}");
            throw new SubscriptionException("Provider unreachable", innerException: ex);
        }
    }

    public async Task<int> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        try
        {
            var existing = await ReadCustomerByReferenceAsync(userId, ct);
            if (existing != null)
            {
                _logger.LogInformation($"Found existing customer for user {userId}: {existing.Id}");
                return existing.Id ?? 0;
            }
        }
        catch (SubscriptionException ex) when (ex.HttpStatusCode == 404)
        {
            _logger.LogInformation($"Customer not found for user {userId}, creating new");
        }

        return await CreateCustomerAsync(userId, email, firstName, lastName, ct);
    }

    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if ((int)ex.Error.StatusCode == 404)
            {
                _logger.LogInformation($"Customer with reference {reference} not found");
                throw new SubscriptionException("Customer not found", 404, ex);
            }
            _logger.LogError($"Failed to read customer by reference: HTTP {(int)ex.Error.StatusCode}");
            throw new SubscriptionException($"Failed to read customer", (int)ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError($"Failed to parse customer response: {ex.Message}");
            throw new SubscriptionException("The provider returned an invalid response.", innerException: ex);
        }
    }

    private async Task<int> CreateCustomerAsync(string userId, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        try
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = userId
                }
            };

            var response = await _client.Customers.CreateCustomer(body: createRequest, ct: ct);
            var customer = response.Customer;
            if (customer?.Id == null)
            {
                throw new SubscriptionException("Customer created but ID is missing");
            }

            _logger.LogInformation($"Created customer for user {userId}: {customer.Id}");
            return customer.Id.Value;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var validationError))
            {
                var message = "Validation error";
                _logger.LogError($"Customer creation validation failed: {message}");
                throw new SubscriptionException(message, 422, ex);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError($"Customer creation failed: HTTP {(int)rawError.StatusCode}");
                throw new SubscriptionException("Failed to create customer", (int)rawError.StatusCode, ex);
            }
            throw new SubscriptionException("Failed to create customer", innerException: ex);
        }
        catch (JsonException ex) when (ex.InnerException is not null)
        {
            _logger.LogError($"Failed to parse customer creation error response: {ex.Message}");
            throw new SubscriptionException("The provider rejected the request.", 422, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError($"Failed to parse customer creation response: {ex.Message}");
            throw new SubscriptionException("The provider returned an invalid response.", innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError($"Connection failed while creating customer: {ex.Message}");
            throw new SubscriptionException("Provider unreachable", innerException: ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle, string userId, CancellationToken ct = default)
    {
        try
        {
            var createRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    Reference = userId
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(body: createRequest, ct: ct);
            var subscription = response.Subscription;
            if (subscription?.Id == null)
            {
                throw new SubscriptionException("Subscription created but ID is missing");
            }

            _logger.LogInformation($"Created subscription {subscription.Id} for customer {customerId}");
            return new SubscriptionDto
            {
                Id = subscription.Id.Value,
                CustomerId = customerId,
                ProductHandle = "",
                State = "active",
                CreatedAt = DateTimeOffset.UtcNow,
                NextBillingAt = null
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validationError))
            {
                var message = string.Join("; ", validationError.Errors ?? new List<string>());
                _logger.LogError($"Subscription creation validation failed: {message}");
                throw new SubscriptionException(message, 422, ex);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError($"Subscription creation failed: HTTP {(int)rawError.StatusCode}");
                throw new SubscriptionException("Failed to create subscription", (int)rawError.StatusCode, ex);
            }
            throw new SubscriptionException("Failed to create subscription", innerException: ex);
        }
        catch (JsonException ex) when (ex.InnerException is not null)
        {
            _logger.LogError($"Failed to parse subscription creation error response: {ex.Message}");
            throw new SubscriptionException("The provider rejected the request.", 422, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError($"Failed to parse subscription creation response: {ex.Message}");
            throw new SubscriptionException("The provider returned an invalid response.", innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError($"Connection failed while creating subscription: {ex.Message}");
            throw new SubscriptionException("Provider unreachable", innerException: ex);
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct);
            var subs = new List<SubscriptionDto>();
            foreach (var response in subscriptions)
            {
                var sub = response.Subscription;
                if (sub?.Id != null)
                {
                    subs.Add(new SubscriptionDto
                    {
                        Id = sub.Id.Value,
                        CustomerId = customerId,
                        ProductHandle = "",
                        State = "active",
                        CreatedAt = DateTimeOffset.UtcNow,
                        NextBillingAt = null
                    });
                }
            }
            return subs;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Failed to list subscriptions: HTTP {(int)ex.Error.StatusCode}");
            throw new SubscriptionException("Failed to retrieve subscriptions", (int)ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError($"Failed to parse subscriptions response: {ex.Message}");
            throw new SubscriptionException("The provider returned an invalid response.", innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError($"Connection failed while fetching subscriptions: {ex.Message}");
            throw new SubscriptionException("Provider unreachable", innerException: ex);
        }
    }
}
