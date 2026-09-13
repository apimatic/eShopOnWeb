using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public sealed class MaxioService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(IOptions<MaxioSettings> settings, ILogger<MaxioService> logger)
    {
        _logger = logger;
        _productFamilyHandle = settings.Value.ProductFamilyHandle;

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials
            {
                Username = settings.Value.ApiKey,
                Password = "x"
            },
            Environment = ServerEnvironment.Us
        };

        if (!string.IsNullOrEmpty(settings.Value.Subdomain))
            options.Server.Production.Us.Site = settings.Value.Subdomain;

        if (!string.IsNullOrEmpty(settings.Value.BaseUrl))
            options.Server.Production.Us.BaseUrl = settings.Value.BaseUrl;

        var httpClient = new HttpClient();
        _client = new MaxioAdvancedBillingClient(httpClient, options);
    }

    public async Task<int> EnsureCustomerAsync(
        string email,
        string firstName,
        string lastName,
        string reference,
        CancellationToken ct)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(reference, ct);
            var customerId = existing.Customer?.Id;
            if (customerId.HasValue)
            {
                _logger.LogDebug("Found existing Maxio customer {CustomerId} for reference {Reference}", customerId.Value, reference);
                return customerId.Value;
            }
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("No Maxio customer found for reference {Reference}, creating new customer", reference);
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var response = await _client.Customers.CreateCustomer(request, ct);
        var id = response.Customer?.Id
            ?? throw new InvalidOperationException("Maxio customer creation returned no ID");

        _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}", id, reference);
        return id;
    }

    public async Task<SubscriptionResponse> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string subscriptionReference,
        CancellationToken ct)
    {
        var existingSubs = await _client.Customers.ListCustomerSubscriptions(customerId, ct);
        var existing = existingSubs.FirstOrDefault(s =>
            s.Subscription?.Reference == subscriptionReference &&
            s.Subscription?.State?.Value != "canceled" &&
            s.Subscription?.State?.Value != "expired");

        if (existing?.Subscription != null)
        {
            _logger.LogInformation("Found existing subscription {SubscriptionId} for reference {Reference}", existing.Subscription.Id, subscriptionReference);
            return existing;
        }

        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = subscriptionReference
            }
        };

        return await _client.Subscriptions.CreateSubscription(request, ct);
    }

    public async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken ct)
    {
        return await _client.Customers.ListCustomerSubscriptions(customerId, ct);
    }

    public async Task<List<SubscriptionPlanDto>> ListSubscriptionPlansAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlanDto>();
        var handles = new[] { "eshop-pro", "basic-plan" };

        foreach (var handle in handles)
        {
            try
            {
                var response = await _client.Products.ReadProductByHandle(handle, ct);
                var product = response.Product;
                if (product != null)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id ?? 0,
                        Name = product.Name ?? string.Empty,
                        Handle = product.Handle ?? string.Empty,
                        Description = product.Description ?? string.Empty,
                        PriceInCents = product.PriceInCents ?? 0,
                        Interval = product.Interval ?? 0,
                        IntervalUnit = product.IntervalUnit?.Value ?? string.Empty,
                        RequireCreditCard = product.RequireCreditCard ?? false
                    });
                }
            }
            catch (SdkException<RawError> ex)
            {
                _logger.LogWarning(ex, "Failed to fetch product with handle {Handle}", handle);
            }
        }

        return plans;
    }

    public async Task<int> GetCustomerIdByReferenceAsync(string reference, CancellationToken ct)
    {
        var response = await _client.Customers.ReadCustomerByReference(reference, ct);
        return response.Customer?.Id
            ?? throw new InvalidOperationException($"No customer found for reference {reference}");
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
