using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionService
{
    Task<CustomerInfo> EnsureCustomerAsync(string email, CancellationToken ct = default);
    Task<SubscriptionSummary[]> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);
    Task<SubscriptionSummary> CreateSubscriptionAsync(int customerId, string customerReference, string productHandle, CancellationToken ct = default);
    Task<PlanInfo[]> GetPlansAsync(CancellationToken ct = default);
}

public record CustomerInfo(int Id, string Reference, string Email);
public record SubscriptionSummary(int Id, string Handle, string State, DateTimeOffset? NextBillingDate);
public record PlanInfo(string Handle, string Name, decimal Price, string FamilyHandle);

public class SubscriptionService : ISubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IConfiguration _config;
    private readonly ILogger<SubscriptionService> _logger;
    private static readonly ConcurrentDictionary<string, int> _customerCache = new();

    public SubscriptionService(MaxioAdvancedBillingClient client, IConfiguration config, ILogger<SubscriptionService> logger)
    {
        _client = client;
        _config = config;
        _logger = logger;
    }

    public async Task<CustomerInfo> EnsureCustomerAsync(string email, CancellationToken ct = default)
    {
        if (_customerCache.TryGetValue(email, out var cachedId))
        {
            return new CustomerInfo(cachedId, email, email);
        }

        try
        {
            var list = await _client.Customers.ListCustomers(
                direction: null,
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                q: email,
                page: 1,
                perPage: 10,
                ct: ct);
            var existing = list?.FirstOrDefault(r => r.Customer?.Email == email || r.Customer?.Reference == email);
            if (existing != null && existing.Customer != null)
            {
                _customerCache[email] = (int)(existing.Customer.Id ?? 0);
                return new CustomerInfo((int)(existing.Customer.Id ?? 0), existing.Customer.Reference ?? email, existing.Customer.Email ?? email);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer list failed for {Email}", email);
        }

        try
        {
            var createReq = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    Email = email,
                    FirstName = "Customer",
                    LastName = "User",
                    Reference = email
                }
            };
            var resp = await _client.Customers.CreateCustomer(createReq, ct: ct);
            if (resp?.Customer != null)
            {
                _customerCache[email] = (int)(resp.Customer.Id ?? 0);
                return new CustomerInfo((int)(resp.Customer.Id ?? 0), resp.Customer.Reference ?? email, resp.Customer.Email ?? email);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Maxio customer for {Email}", email);
            throw;
        }

        throw new InvalidOperationException("Unable to resolve or create Maxio customer.");
    }

    public async Task<SubscriptionSummary[]> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        try
        {
            var list = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
            return list?.Select(r =>
            {
                var s = r.Subscription;
                if (s == null) return new SubscriptionSummary(0, "", "", null);
                return new SubscriptionSummary(
                    (int)(s.Id ?? 0),
                    s.NextProductHandle ?? "",
                    s.State?.Value ?? "",
                    s.NextAssessmentAt);
            }).ToArray() ?? Array.Empty<SubscriptionSummary>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list subscriptions for customer {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<SubscriptionSummary> CreateSubscriptionAsync(int customerId, string customerReference, string productHandle, CancellationToken ct = default)
    {
        try
        {
            var req = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerReference = customerReference
                }
            };
            var resp = await _client.Subscriptions.CreateSubscription(req, ct: ct);
            var s = resp?.Subscription;
            if (s == null) throw new InvalidOperationException("Subscription response missing subscription.");
            return new SubscriptionSummary(
                (int)(s.Id ?? 0),
                s.NextProductHandle ?? "",
                s.State?.Value ?? "",
                s.NextAssessmentAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create subscription for customer {CustomerId} handle {Handle}", customerId, productHandle);
            throw;
        }
    }

    public async Task<PlanInfo[]> GetPlansAsync(CancellationToken ct = default)
    {
        var familyHandle = _config["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";
        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyHandle,
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

            return products?.Select(p =>
            {
                var prod = p.Product;
                return new PlanInfo(
                    prod?.Handle ?? "",
                    prod?.Name ?? "",
                    0m,
                    familyHandle);
            }).ToArray() ?? Array.Empty<PlanInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list plans for family {Family}", familyHandle);
            return Array.Empty<PlanInfo>();
        }
    }
}
