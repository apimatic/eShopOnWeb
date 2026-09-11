using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi;

public class MaxioSettings
{
    public const string SectionName = "Maxio";
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}

public interface IMaxioSubscriptionService
{
    Task<List<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto> SubscribeAsync(string userId, string productHandle, CancellationToken ct = default);
    Task<List<SubscriptionDto>> GetMySubscriptionsAsync(string userId, CancellationToken ct = default);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        MaxioSettings settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default)
    {
        var products = await _client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: $"handle:{_settings.ProductFamilyHandle}",
            dateField: null,
            filter: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            includeArchived: null,
            include: null,
            page: 1,
            perPage: 100,
            ct: ct);

        return products
            .Where(p => p.Product != null)
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Product!.Id,
                Name = p.Product.Name,
                Handle = p.Product.Handle,
                Description = p.Product.Description,
                PriceInCents = p.Product.PriceInCents,
                Interval = p.Product.Interval,
                IntervalUnit = p.Product.IntervalUnit?.Value,
                ProductFamilyHandle = p.Product.ProductFamily?.Handle
            })
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userId, string productHandle, CancellationToken ct = default)
    {
        var customer = await EnsureCustomerExistsAsync(userId, ct);

        var response = await _client.Subscriptions.CreateSubscription(
            body: new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customer.Id
                }
            },
            ct: ct);

        var sub = response.Subscription;
        return MapSubscription(sub);
    }

    public async Task<List<SubscriptionDto>> GetMySubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        var customer = await EnsureCustomerExistsAsync(userId, ct);

        var subscriptions = await _client.Customers.ListCustomerSubscriptions(
            customerId: customer.Id!.Value,
            ct: ct);

        return subscriptions
            .Where(s => s.Subscription != null)
            .Select(s => MapSubscription(s.Subscription!))
            .ToList();
    }

    private async Task<Customer> EnsureCustomerExistsAsync(string userId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                reference: userId,
                ct: ct);
            return response.Customer!;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Customer not found for reference {Reference}, creating new customer", userId);

            var createResponse = await _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = "eShop",
                        LastName = "User",
                        Email = $"user-{userId}@eshop.local",
                        Reference = userId
                    }
                },
                ct: ct);

            return createResponse.Customer!;
        }
    }

    private static SubscriptionDto MapSubscription(Subscription sub)
    {
        return new SubscriptionDto
        {
            Id = sub.Id,
            State = sub.State?.Value,
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            NextAssessmentAt = sub.NextAssessmentAt,
            ActivatedAt = sub.ActivatedAt,
            CreatedAt = sub.CreatedAt,
            ProductName = sub.Product?.Name,
            ProductHandle = sub.Product?.Handle,
            BalanceInCents = sub.BalanceInCents,
            TotalRevenueInCents = sub.TotalRevenueInCents
        };
    }
}
