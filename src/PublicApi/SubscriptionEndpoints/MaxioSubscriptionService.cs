using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, MaxioConfiguration config, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _config = config;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _config.ProductFamilyHandle,
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

            return response
                .Where(pr => pr?.Product != null)
                .Select(pr => new SubscriptionPlanDto
                {
                    Handle = pr!.Product!.Handle ?? string.Empty,
                    Name = pr.Product.Name ?? string.Empty,
                    PriceInDollars = (pr.Product.PriceInCents ?? 0) / 100m
                })
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error listing subscription plans");
            throw new InvalidOperationException("Failed to fetch subscription plans", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error listing subscription plans");
            throw new InvalidOperationException("Failed to parse subscription plans response", ex);
        }
    }

    public async Task<(int CustomerId, bool IsNew)> EnsureCustomerAsync(string userId, string? email = null, string? firstName = null, string? lastName = null, CancellationToken ct = default)
    {
        try
        {
            var existing = await ReadCustomerByReferenceAsync(userId, ct);
            return (existing, false);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return (await CreateCustomerAsync(userId, email, firstName, lastName, ct), true);
        }
    }

    private async Task<int> ReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: reference, ct: ct);
            return response.Customer!.Id ?? throw new InvalidOperationException("Customer ID is missing");
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw;
            }
            _logger.LogError(ex, "Error reading customer by reference");
            throw new InvalidOperationException("Failed to read customer", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error reading customer");
            throw new InvalidOperationException("Failed to parse customer response", ex);
        }
    }

    private async Task<int> CreateCustomerAsync(string userId, string? email, string? firstName, string? lastName, CancellationToken ct)
    {
        try
        {
            var body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName ?? string.Empty,
                    LastName = lastName ?? string.Empty,
                    Email = email ?? string.Empty,
                    Reference = userId
                }
            };

            var response = await _client.Customers.CreateCustomer(body: body, ct: ct);
            return response.Customer!.Id ?? throw new InvalidOperationException("Customer ID is missing");
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var err422))
            {
                _logger.LogError("Customer creation returned 422 error response");
                // If customer creation fails due to duplicate reference, try to read the existing customer
                try
                {
                    return await ReadCustomerByReferenceAsync(userId, ct);
                }
                catch (SdkException<RawError>)
                {
                    // If read also fails, throw the original creation error
                    _logger.LogError(ex, "Error creating customer and failed to read existing");
                    throw new InvalidOperationException("Failed to create or find customer", ex);
                }
            }
            _logger.LogError(ex, "Error creating customer");
            throw new InvalidOperationException("Failed to create customer", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error creating customer");
            throw new InvalidOperationException("Failed to parse customer creation response", ex);
        }
    }

    public async Task<CreateSubscriptionResponse> CreateSubscriptionAsync(string userId, string productHandle, CancellationToken ct = default)
    {
        var (customerId, _) = await EnsureCustomerAsync(userId, ct: ct);

        try
        {
            var body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerReference = userId,
                    ProductHandle = productHandle,
                    DeferSignup = true
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(body: body, ct: ct);

            return new CreateSubscriptionResponse
            {
                SubscriptionId = response.Subscription!.Id ?? 0,
                State = response.Subscription.State?.Value ?? string.Empty,
                ProductHandle = response.Subscription.Product?.Handle ?? string.Empty
            };
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var err422))
            {
                _logger.LogError("Subscription creation returned 422: {errors}", string.Join(", ", err422.Errors ?? new List<string>()));
            }
            _logger.LogError(ex, "Error creating subscription");
            throw new InvalidOperationException("Failed to create subscription", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error creating subscription");
            throw new InvalidOperationException("Failed to parse subscription response", ex);
        }
    }

    public async Task<List<UserSubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var customerId = await ReadCustomerByReferenceAsync(userId, ct);
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct);

            var activeStates = new[]
            {
                SubscriptionState.Active,
                SubscriptionState.Assessing,
                SubscriptionState.Trialing,
                SubscriptionState.Pending
            };

            return subscriptions
                .Where(s => s?.Subscription != null)
                .Where(s => activeStates.Contains(s!.Subscription!.State))
                .Select(s => new UserSubscriptionDto
                {
                    Id = s!.Subscription!.Id ?? 0,
                    State = s.Subscription.State?.Value ?? string.Empty,
                    ProductHandle = s.Subscription.Product?.Handle ?? string.Empty,
                    ProductName = s.Subscription.Product?.Name ?? string.Empty
                })
                .ToList();
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new List<UserSubscriptionDto>();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error listing customer subscriptions");
            throw new InvalidOperationException("Failed to fetch subscriptions", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error listing subscriptions");
            throw new InvalidOperationException("Failed to parse subscriptions response", ex);
        }
    }
}
