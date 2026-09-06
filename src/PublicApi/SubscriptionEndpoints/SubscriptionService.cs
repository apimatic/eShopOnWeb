using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionService
{
    Task<SubscriptionPlanDto[]> GetSubscriptionPlansAsync(CancellationToken ct);
    Task<SubscriptionDto> CreateSubscriptionAsync(string userReference, string planHandle, CancellationToken ct);
    Task<SubscriptionDto[]> GetUserSubscriptionsAsync(string userReference, CancellationToken ct);
}

public sealed class SubscriptionService : ISubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly string _productFamilyHandle;

    public SubscriptionService(
        MaxioAdvancedBillingClient client,
        IConfiguration configuration,
        ILogger<SubscriptionService> logger)
    {
        _client = client;
        _logger = logger;
        _productFamilyHandle = configuration["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";
    }

    public async Task<SubscriptionPlanDto[]> GetSubscriptionPlansAsync(CancellationToken ct)
    {
        try
        {
            var products = await _client.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: ct);

            var plans = new List<SubscriptionPlanDto>();

            foreach (var productResponse in products)
            {
                var product = productResponse.Product;
                if (product?.Handle == null) continue;

                // Hard-code the plan handles we expect
                if (product.Handle != "eshop-pro" && product.Handle != "basic-plan")
                    continue;

                plans.Add(new SubscriptionPlanDto
                {
                    Handle = product.Handle,
                    Name = product.Name ?? product.Handle,
                    PricePointHandle = product.ProductPricePointHandle ?? product.Handle,
                    PriceInCents = 0,
                    Interval = 1,
                    IntervalUnit = "month"
                });
            }

            return plans.ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Error fetching subscription plans: HTTP {(int)ex.Error.StatusCode}");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected error fetching subscription plans: {ex.Message}");
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string userReference,
        string planHandle,
        CancellationToken ct)
    {
        try
        {
            var customer = await EnsureCustomerExistsAsync(userReference, ct);

            var body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customer.Id,
                    ProductHandle = planHandle
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(body: body, ct: ct);

            if (response.Subscription == null)
            {
                throw new InvalidOperationException("Subscription creation returned no subscription data");
            }

            return MapSubscriptionToDto(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            _logger.LogError($"Subscription creation failed");
            if (ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError($"HTTP {(int)rawError.StatusCode}");
            }
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected error creating subscription: {ex.Message}");
            throw;
        }
    }

    public async Task<SubscriptionDto[]> GetUserSubscriptionsAsync(string userReference, CancellationToken ct)
    {
        try
        {
            var customer = await LookupCustomerByReferenceAsync(userReference, ct);

            if (customer == null)
            {
                return Array.Empty<SubscriptionDto>();
            }

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                customerId: customer.Id ?? 0,
                ct: ct);

            return subscriptions
                .Where(s => s.Subscription?.State == SubscriptionState.Active)
                .Select(s => MapSubscriptionToDto(s.Subscription!))
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Error fetching subscriptions: HTTP {(int)ex.Error.StatusCode}");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected error fetching subscriptions: {ex.Message}");
            throw;
        }
    }

    private async Task<Customer> EnsureCustomerExistsAsync(string userReference, CancellationToken ct)
    {
        var existing = await LookupCustomerByReferenceAsync(userReference, ct);
        if (existing != null)
        {
            return existing;
        }

        var createBody = new MaxioAdvancedBilling.Models.CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = "User",
                LastName = userReference,
                Email = $"{userReference}@eshop.local",
                Reference = userReference
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body: createBody, ct: ct);
            return response.Customer ?? throw new InvalidOperationException("Customer creation returned no customer data");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            _logger.LogError($"Customer creation failed");
            if (ex.Error.TryGetRawError(out var rawError))
            {
                _logger.LogError($"HTTP {(int)rawError.StatusCode}");
            }
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected error creating customer: {ex.Message}");
            throw;
        }
    }

    private async Task<Customer?> LookupCustomerByReferenceAsync(string userReference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                reference: userReference,
                ct: ct);

            return response.Customer;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
    }

    private static SubscriptionDto MapSubscriptionToDto(Subscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            State = subscription.State?.Value ?? "unknown",
            CurrentPeriodStartsAt = null,
            NextAssessmentAt = null,
            ProductHandle = "",
            PricePointHandle = ""
        };
    }
}

public sealed class SubscriptionPlanDto
{
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public string PricePointHandle { get; set; } = "";
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}

public sealed class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public string ProductHandle { get; set; } = "";
    public string PricePointHandle { get; set; } = "";
}
