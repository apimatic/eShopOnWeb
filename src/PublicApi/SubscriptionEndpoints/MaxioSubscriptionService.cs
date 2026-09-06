using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        MaxioConfiguration config,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _config = config;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default(CancellationToken))
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

            return response.Select(pr => new SubscriptionPlanDto
            {
                Id = pr.Product.Id ?? 0,
                Handle = pr.Product.Handle ?? string.Empty,
                Name = pr.Product.Name ?? string.Empty,
                PriceInCents = pr.Product.PriceInCents ?? 0,
                Interval = pr.Product.Interval ?? 1,
                IntervalUnit = pr.Product.IntervalUnit?.Value ?? "month"
            }).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Error listing plans: {ex.Error.StatusCode}");
            throw new MaxioException($"Failed to list subscription plans: {ex.Error.StatusCode}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected error listing plans: {ex.Message}");
            throw new MaxioException("An unexpected error occurred while listing subscription plans.", ex);
        }
    }

    public async Task<int> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        try
        {
            // Try to look up existing customer
            var existingCustomer = await ReadCustomerByReferenceAsync(userId, ct);
            if (existingCustomer != null)
            {
                return existingCustomer.Id ?? 0;
            }
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Customer doesn't exist, proceed to create
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error reading customer: {ex.Message}");
            throw;
        }

        // Create customer
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

        try
        {
            var response = await _client.Customers.CreateCustomer(body: createRequest, ct: ct);
            return response.Customer.Id ?? 0;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResponse))
            {
                var errors = errorResponse.Errors?.ToString() ?? "Unknown error";
                _logger.LogError($"Customer creation error: {errors}");
                throw new MaxioException($"Failed to create customer: {errors}", ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError($"Customer creation error: {raw.StatusCode}");
                throw new MaxioException($"Failed to create customer: {raw.StatusCode}", ex);
            }
            throw new MaxioException("Failed to create customer", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError($"JSON error during customer creation: {ex.Message}");
            throw new MaxioException("Failed to process customer creation response", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected error creating customer: {ex.Message}");
            throw new MaxioException("An unexpected error occurred while creating customer.", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string planHandle,
        string customerId,
        CancellationToken ct = default)
    {
        var createRequest = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerReference = customerId,
                DeferSignup = false
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body: createRequest, ct: ct);
            return MapSubscriptionResponse(response);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorResponse))
            {
                var errors = errorResponse.Errors?.FirstOrDefault();
                _logger.LogError($"Subscription creation error: {errors}");
                throw new MaxioException($"Failed to create subscription: {errors}", ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError($"Subscription creation error: {raw.StatusCode}");
                throw new MaxioException($"Failed to create subscription: {raw.StatusCode}", ex);
            }
            throw new MaxioException("Failed to create subscription", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError($"JSON error during subscription creation: {ex.Message}");
            throw new MaxioException("Failed to process subscription creation response", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected error creating subscription: {ex.Message}");
            throw new MaxioException("An unexpected error occurred while creating subscription.", ex);
        }
    }

    public async Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct);
            return response.Select(MapSubscriptionResponse).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Error listing customer subscriptions: {ex.Error.StatusCode}");
            throw new MaxioException($"Failed to list customer subscriptions: {ex.Error.StatusCode}", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError($"JSON error listing subscriptions: {ex.Message}");
            throw new MaxioException("Failed to process subscriptions response", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected error listing subscriptions: {ex.Message}");
            throw new MaxioException("An unexpected error occurred while listing subscriptions.", ex);
        }
    }

    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        var response = await _client.Customers.ReadCustomerByReference(reference: reference, ct: ct);
        return response.Customer;
    }

    private SubscriptionDto MapSubscriptionResponse(SubscriptionResponse response)
    {
        var sub = response.Subscription;
        return new SubscriptionDto
        {
            Id = sub.Id ?? 0,
            Reference = sub.Reference ?? string.Empty,
            State = sub.State?.Value ?? string.Empty,
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            NextAssessmentAt = sub.NextAssessmentAt,
            Product = sub.Product != null ? new SubscriptionPlanDto
            {
                Id = sub.Product.Id ?? 0,
                Handle = sub.Product.Handle ?? string.Empty,
                Name = sub.Product.Name ?? string.Empty,
                PriceInCents = sub.Product.PriceInCents ?? 0,
                Interval = sub.Product.Interval ?? 1,
                IntervalUnit = sub.Product.IntervalUnit?.Value ?? "month"
            } : null
        };
    }
}

public class MaxioException : Exception
{
    public MaxioException(string message) : base(message) { }
    public MaxioException(string message, Exception innerException) : base(message, innerException) { }
}
