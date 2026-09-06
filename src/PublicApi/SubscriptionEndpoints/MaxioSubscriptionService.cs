using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SubscriptionPlanDto[]> GetPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _settings.ProductFamilyHandle,
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

            return response.Select(p => new SubscriptionPlanDto
            {
                Id = p.Product?.Id ?? 0,
                Handle = p.Product?.Handle ?? string.Empty,
                Name = p.Product?.Name ?? string.Empty,
                PriceInCents = p.Product?.PriceInCents,
                Interval = p.Product?.Interval,
                IntervalUnit = p.Product?.IntervalUnit?.ToString()
            }).ToArray();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var error))
            {
                _logger.LogError("Maxio product family not found: {Error}", error);
                throw new InvalidOperationException($"Product family '{_settings.ProductFamilyHandle}' not found in Maxio", ex);
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                _logger.LogError("Maxio error listing products: HTTP {StatusCode}", (int)raw.StatusCode);
                throw new InvalidOperationException($"Failed to list subscription plans: {raw.ReadAsString()}", ex);
            }
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string productHandle, CancellationToken ct = default)
    {
        var customerId = await GetOrCreateCustomerAsync(userId, ct);

        try
        {
            var createSubRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    Reference = $"sub-{userId}-{Guid.NewGuid()}"
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(
                body: createSubRequest,
                ct: ct);

            return new SubscriptionDto
            {
                Id = response.Subscription?.Id ?? 0,
                State = response.Subscription?.State?.ToString(),
                ProductHandle = response.Subscription?.Product?.Handle,
                ProductId = response.Subscription?.Product?.Id,
                CurrentPeriodEndsAt = response.Subscription?.CurrentPeriodEndsAt,
                NextAssessmentAt = response.Subscription?.NextAssessmentAt
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                _logger.LogError("Maxio validation error creating subscription: {Errors}", errorList);
                throw new InvalidOperationException("Failed to create subscription: validation error", ex);
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                _logger.LogError("Maxio error creating subscription: HTTP {StatusCode}", (int)raw.StatusCode);
                throw new InvalidOperationException($"Failed to create subscription: {raw.ReadAsString()}", ex);
            }
            throw;
        }
    }

    public async Task<SubscriptionDto[]> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var customer = await LookupCustomerByReferenceAsync(userId, ct);
            if (customer == null)
            {
                return [];
            }

            var customerId = customer.Customer?.Id ?? 0;
            var response = await _client.Customers.ListCustomerSubscriptions(
                customerId: customerId,
                ct: ct);

            return response.Select(s => new SubscriptionDto
            {
                Id = s.Subscription?.Id ?? 0,
                State = s.Subscription?.State?.ToString(),
                ProductHandle = s.Subscription?.Product?.Handle,
                ProductId = s.Subscription?.Product?.Id,
                CurrentPeriodEndsAt = s.Subscription?.CurrentPeriodEndsAt,
                NextAssessmentAt = s.Subscription?.NextAssessmentAt
            }).ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Maxio error listing subscriptions: HTTP {StatusCode}", (int)ex.Error.StatusCode);
            throw new InvalidOperationException($"Failed to list subscriptions: {ex.Error.ReadAsString()}", ex);
        }
    }

    private async Task<int> GetOrCreateCustomerAsync(string userId, CancellationToken ct)
    {
        var customer = await LookupCustomerByReferenceAsync(userId, ct);
        if (customer != null)
        {
            return customer.Customer?.Id ?? 0;
        }

        return await CreateCustomerAsync(userId, ct);
    }

    private async Task<MaxioAdvancedBilling.Models.CustomerResponse?> LookupCustomerByReferenceAsync(string userId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                reference: userId,
                ct: ct);
            return response;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            _logger.LogError("Maxio error looking up customer: HTTP {StatusCode}", (int)ex.Error.StatusCode);
            throw new InvalidOperationException($"Failed to lookup customer: {ex.Error.ReadAsString()}", ex);
        }
    }

    private async Task<int> CreateCustomerAsync(string userId, CancellationToken ct)
    {
        try
        {
            var createCustRequest = new MaxioAdvancedBilling.Models.CreateCustomerRequest
            {
                Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                {
                    FirstName = "User",
                    LastName = userId.Split('@')[0],
                    Email = userId,
                    Reference = userId
                }
            };

            var response = await _client.Customers.CreateCustomer(
                body: createCustRequest,
                ct: ct);

            return response.Customer?.Id ?? 0;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var custError))
            {
                _logger.LogError("Maxio customer creation error: {Errors}", custError);
                throw new InvalidOperationException("Failed to create customer: validation error", ex);
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                _logger.LogError("Maxio error creating customer: HTTP {StatusCode}", (int)raw.StatusCode);
                throw new InvalidOperationException($"Failed to create customer: {raw.ReadAsString()}", ex);
            }
            throw;
        }
    }
}
