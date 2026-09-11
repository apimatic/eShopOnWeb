using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.PublicApi;

public interface IMaxioBillingService
{
    Task<IReadOnlyList<Product>> GetPlansAsync(CancellationToken ct = default);
    Task<Subscription?> SubscribeAsync(string userId, string planHandle, CancellationToken ct = default);
    Task<IReadOnlyList<Subscription>> GetMySubscriptionsAsync(string userId, CancellationToken ct = default);
}

public class MaxioBillingService : IMaxioBillingService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _familyHandle;
    private readonly ConcurrentDictionary<string, int> _userToCustomerId = new();

    public MaxioBillingService(IConfiguration config, IHttpClientFactory factory)
    {
        var apiKey = config["Maxio:ApiKey"]!;
        var subdomain = config["Maxio:Subdomain"]!;
        var familyHandle = config["Maxio:ProductFamilyHandle"]!;
        _familyHandle = familyHandle ?? "eshop-subscribe";

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" },
            Retry = RetryOptions.Default()
        };

        // Site override
        if (!string.IsNullOrWhiteSpace(subdomain))
        {
            options.Server.Production.Us.Site = subdomain;
        }

        // Optional base URL override
        var baseUrl = config["Maxio:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            options.Server.Production.Us.BaseUrl = baseUrl;
            // Also set environment to match if needed; keep Us
        }

        var http = factory.CreateClient();
        _client = new MaxioAdvancedBillingClient(http, options);
    }

    public async Task<IReadOnlyList<Product>> GetPlansAsync(CancellationToken ct = default)
    {
        var products = await _client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: _familyHandle,
            dateField: null,
            filter: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            includeArchived: false,
            include: null,
            page: 1,
            perPage: 20,
            ct: ct);

        var list = new List<Product>();
        foreach (var resp in products)
        {
            if (resp.Product != null)
                list.Add(resp.Product);
        }
        return list;
    }

    public async Task<Subscription?> SubscribeAsync(string userId, string planHandle, CancellationToken ct = default)
    {
        // Idempotent customer resolve / create
        int customerId = await ResolveCustomerAsync(userId, ct);

        // Check existing subscriptions to avoid duplicates (defensive)
        var existing = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
        foreach (var subResp in existing)
        {
            if (subResp.Subscription != null 
                && subResp.Subscription.Product?.Handle == planHandle 
                && subResp.Subscription.State == "active")
                return subResp.Subscription;
        }

        var req = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerReference = userId,
                ProductHandle = planHandle,
                ProductId = null,
                CustomerId = null,
                CustomerAttributes = null
            }
        };

        var result = await _client.Subscriptions.CreateSubscription(req, ct: ct);
        return result.Subscription;
    }

    public async Task<IReadOnlyList<Subscription>> GetMySubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        int customerId = await ResolveCustomerAsync(userId, ct);
        var subs = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
        var list = new List<Subscription>();
        foreach (var r in subs)
        {
            if (r.Subscription != null)
                list.Add(r.Subscription);
        }
        return list;
    }

    private async Task<int> ResolveCustomerAsync(string userId, CancellationToken ct)
    {
        if (_userToCustomerId.TryGetValue(userId, out var cached))
            return cached;

        // Try reference lookup first
        try
        {
            var byRef = await _client.Customers.ReadCustomerByReference(userId, ct: ct);
            if (byRef.Customer != null && byRef.Customer.Id.HasValue)
            {
                _userToCustomerId[userId] = (int)byRef.Customer.Id.Value;
                return (int)byRef.Customer.Id.Value;
            }
        }
        catch (SdkException<RawError>)
        {
            // Not found by reference; proceed to create
        }

        // Search by email if we can get it? We only have userId reference.
        // Create with reference = userId
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = "Shopper",
                LastName = "User",
                Email = $"{userId}@eshop.local",
                Reference = userId
            }
        };

        var created = await _client.Customers.CreateCustomer(request, ct: ct);
        var idRaw = created.Customer?.Id ?? throw new InvalidOperationException("Customer created without Id");
        var id = (int)idRaw;
        _userToCustomerId[userId] = id;
        return id;
    }
}
