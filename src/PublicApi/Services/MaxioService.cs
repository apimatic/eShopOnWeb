using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Authentication;
using AdvancedBilling.Standard.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.PublicApi.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioService : IMaxioService
{
    private readonly AdvancedBillingClient _client;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioService> _logger;
    private readonly Dictionary<string, int> _customerCache = new();

    public MaxioService(IOptions<MaxioSettings> settings, ILogger<MaxioService> logger)
    {
        _logger = logger;
        var s = settings.Value;

        if (string.IsNullOrWhiteSpace(s.ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set MAXIO_API_KEY environment variable.");

        if (string.IsNullOrWhiteSpace(s.Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain is not configured. Set MAXIO_SITE_SUBDOMAIN environment variable.");

        _productFamilyHandle = s.ProductFamilyHandle ?? string.Empty;

        var builder = new AdvancedBillingClient.Builder()
            .BasicAuthCredentials(new BasicAuthModel.Builder(s.ApiKey, "x").Build())
            .Site(s.Subdomain)
            .Environment(AdvancedBilling.Standard.Environment.US);

        if (!string.IsNullOrWhiteSpace(s.BaseUrl))
        {
            var customHttpClient = new HttpClient { BaseAddress = new Uri(s.BaseUrl.TrimEnd('/') + "/") };
            builder.HttpClientConfig(config =>
            {
                config.HttpClientInstance(customHttpClient, true);
            });
        }

        _client = builder.Build();
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync()
    {
        var result = new List<SubscriptionPlanDto>();

        try
        {
            var familiesResponse = await _client.ProductFamiliesController.ListProductFamiliesAsync(
                new ListProductFamiliesInput());

            var matchingFamilies = familiesResponse
                .Select(r => r.ProductFamily)
                .Where(f => f != null && f.Handle == _productFamilyHandle);

            foreach (var family in matchingFamilies)
            {
                var productsInput = new ListProductsForProductFamilyInput
                {
                    ProductFamilyId = family.Id?.ToString()
                };
                var productsResponse = await _client.ProductFamiliesController
                    .ListProductsForProductFamilyAsync(productsInput);

                foreach (var productResp in productsResponse)
                {
                    var product = productResp.Product;
                    if (product == null) continue;

                    result.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id ?? 0,
                        Name = product.Name ?? string.Empty,
                        Handle = product.Handle ?? string.Empty,
                        Description = product.Description ?? string.Empty,
                        Price = (decimal)(product.PriceInCents ?? 0) / 100m,
                        IntervalUnit = product.IntervalUnit?.ToString() ?? "month",
                        RequireCreditCard = product.RequireCreditCard ?? false
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing Maxio subscription plans");
            throw;
        }

        return result;
    }

    public async Task<SubscriptionResult> CreateSubscriptionAsync(string email, string firstName, string lastName, string productHandle)
    {
        try
        {
            var customerId = await EnsureCustomerAsync(email, firstName, lastName);

            var subscription = new CreateSubscription
            {
                CustomerId = customerId,
                ProductHandle = productHandle
            };
            var request = new CreateSubscriptionRequest(subscription);

            var response = await _client.SubscriptionsController.CreateSubscriptionAsync(request);

            var sub = response.Subscription;
            return new SubscriptionResult
            {
                Success = true,
                Subscription = new SubscriptionDto
                {
                    Id = sub.Id ?? 0,
                    State = sub.State?.ToString() ?? "unknown",
                    PlanName = sub.Product?.Name ?? string.Empty,
                    PlanHandle = sub.Product?.Handle ?? string.Empty,
                    Price = (decimal)(sub.Product?.PriceInCents ?? 0) / 100m,
                    CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt?.DateTime,
                    NextBillingDate = (sub.NextAssessmentAt ?? sub.CurrentPeriodEndsAt)?.DateTime,
                    CreatedAt = (sub.CreatedAt ?? DateTimeOffset.UtcNow).DateTime
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Maxio subscription for {Email} plan {ProductHandle}", email, productHandle);
            return new SubscriptionResult
            {
                Success = false,
                ErrorMessage = $"Failed to create subscription: {ex.Message}"
            };
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsByEmailAsync(string email)
    {
        try
        {
            var customerId = await FindCustomerIdByEmailAsync(email);
            if (customerId == null)
            {
                return Array.Empty<SubscriptionDto>();
            }

            var response = await _client.CustomersController.ListCustomerSubscriptionsAsync(customerId.Value);

            return response
                .Select(r => r.Subscription)
                .Where(s => s != null)
                .Select(s => new SubscriptionDto
            {
                Id = s.Id ?? 0,
                State = s.State?.ToString() ?? "unknown",
                PlanName = s.Product?.Name ?? string.Empty,
                PlanHandle = s.Product?.Handle ?? string.Empty,
                Price = (decimal)(s.Product?.PriceInCents ?? 0) / 100m,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt?.DateTime,
                NextBillingDate = (s.NextAssessmentAt ?? s.CurrentPeriodEndsAt)?.DateTime,
                CreatedAt = (s.CreatedAt ?? DateTimeOffset.UtcNow).DateTime
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing Maxio subscriptions for {Email}", email);
            throw;
        }
    }

    private async Task<int> EnsureCustomerAsync(string email, string firstName, string lastName)
    {
        var cached = await FindCustomerIdByEmailAsync(email);
        if (cached.HasValue)
        {
            _logger.LogInformation("Using existing Maxio customer {CustomerId} for {Email}", cached.Value, email);
            return cached.Value;
        }

        _logger.LogInformation("Creating new Maxio customer for {Email}", email);

        var customer = new CreateCustomer
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Reference = email
        };
        var request = new CreateCustomerRequest(customer);

        var response = await _client.CustomersController.CreateCustomerAsync(request);
        var customerId = response.Customer.Id ?? 0;

        _customerCache[email] = customerId;
        _logger.LogInformation("Created Maxio customer {CustomerId} for {Email}", customerId, email);

        return customerId;
    }

    private async Task<int?> FindCustomerIdByEmailAsync(string email)
    {
        if (_customerCache.TryGetValue(email, out var cachedId))
        {
            return cachedId;
        }

        try
        {
            var response = await _client.CustomersController.ReadCustomerByReferenceAsync(email);
            if (response?.Customer?.Id.HasValue == true)
            {
                _customerCache[email] = response.Customer.Id.Value;
                return response.Customer.Id.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Customer not found by reference {Email}", email);
        }

        return null;
    }
}
