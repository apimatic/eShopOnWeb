using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public interface IMaxioBillingService
{
    Task<SubscriptionDto?> EnsureSubscriptionAsync(string userReference, string productHandle, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userReference, CancellationToken ct = default);
    Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct = default);
}

public class MaxioBillingService : IMaxioBillingService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;

    public MaxioBillingService(IOptions<MaxioSettings> settings, HttpClient httpClient)
    {
        _settings = settings.Value;
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = _settings.ApiKey, Password = "x" },
            Retry = RetryOptions.Default(),
        };

        if (!string.IsNullOrEmpty(_settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = _settings.BaseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = _settings.Subdomain;
        }

        _client = new MaxioAdvancedBillingClient(httpClient, options);
    }

    public async Task<SubscriptionDto?> EnsureSubscriptionAsync(string userReference, string productHandle, CancellationToken ct = default)
    {
        // Idempotent customer
        int customerId = await EnsureCustomerAsync(userReference, ct);

        // Check existing subscriptions for this customer
        var existing = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
        if (existing != null)
        {
            foreach (var resp in existing)
            {
                var sub = resp.Subscription;
                if (sub == null) continue;

                // Best-effort match by product handle; defensive: compare product id if handle missing
                string? subHandle = sub.Product?.Handle;
                if (subHandle == productHandle || (sub.Product != null && sub.Product.Handle == productHandle))
                {
                    return ToDto(sub);
                }
            }
        }

        // Resolve product by handle to confirm
        var prodResp = await _client.Products.ReadProductByHandle(productHandle, ct: ct);
        if (prodResp?.Product == null || string.IsNullOrEmpty(prodResp.Product.Handle))
            return null;

        var createReq = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
            }
        };

        var subResp = await _client.Subscriptions.CreateSubscription(createReq, ct: ct);
        return subResp?.Subscription != null ? ToDto(subResp.Subscription) : null;
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userReference, CancellationToken ct = default)
    {
        int customerId = await EnsureCustomerAsync(userReference, ct);
        var list = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
        var result = new List<SubscriptionDto>();
        if (list != null)
        {
            foreach (var resp in list)
            {
                if (resp.Subscription != null)
                    result.Add(ToDto(resp.Subscription));
            }
        }
        return result;
    }

    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        // Known handles from catalog; resolve by handle directly
        var handles = new[] { "eshop-pro", "basic-plan" };
        var result = new List<PlanDto>();
        foreach (var h in handles)
        {
            try
            {
                var resp = await _client.Products.ReadProductByHandle(h, ct: ct);
                if (resp?.Product != null)
                {
                    result.Add(new PlanDto
                    {
                        Handle = resp.Product.Handle ?? h,
                        Name = resp.Product.Name ?? h,
                        Price = 0m,
                        Currency = "USD",
                    });
                }
            }
            catch { /* defensive skip */ }
        }
        return result;
    }

    private async Task<int> EnsureCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var readResp = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            if (readResp?.Customer != null)
                return (int)readResp.Customer.Id!;
        }
        catch (Exception)
        {
            // Defensive: reference lookup may fail; fall through to list
        }

        // Fallback list
        var list = await _client.Customers.ListCustomers(direction: null, dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, q: reference, page: 1, perPage: 20, ct: ct);
        if (list != null)
        {
            foreach (var r in list)
            {
                if (r.Customer != null && (r.Customer.Reference == reference || r.Customer.Email == reference))
                    return (int)r.Customer.Id!;
            }
        }

        var createReq = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                Email = reference,
                FirstName = "User",
                LastName = "Account",
                Reference = reference,
            }
        };
        var createResp = await _client.Customers.CreateCustomer(createReq, ct: ct);
        if (createResp?.Customer != null)
            return (int)createResp.Customer.Id!;

        throw new InvalidOperationException("Failed to create or find Maxio customer.");
    }

    private SubscriptionDto ToDto(Subscription sub)
    {
        return new SubscriptionDto
        {
            Id = (int)sub.Id!,
            State = sub.State.ToString(),
            ProductHandle = sub.Product?.Handle,
            ProductName = sub.Product?.Name,
        };
    }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
}

public class PlanDto
{
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
}
