using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<List<PlanSummary>> GetPlansAsync(CancellationToken ct = default);
    Task<SubscriptionResult> SubscribeAsync(string userEmail, string productHandle, CancellationToken ct = default);
    Task<List<SubscriptionResult>> GetMySubscriptionsAsync(string userEmail, CancellationToken ct = default);
}

public class PlanSummary
{
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? Price { get; set; }
    public string Interval { get; set; } = "";
}

public class SubscriptionResult
{
    public int Id { get; set; }
    public string ProductHandle { get; set; } = "";
    public string State { get; set; } = "";
    public DateTimeOffset? NextBilling { get; set; }
    public string CustomerReference { get; set; } = "";
}

public class MaxioService : IMaxioService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _familyHandle;

    public MaxioService(IConfiguration config, IHttpClientFactory httpFactory)
    {
        var apiKey = config["Maxio:ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey missing");
        var subdomain = config["Maxio:Subdomain"] ?? "cp-exp-1";
        _familyHandle = config["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";
        var baseUrlOverride = config["Maxio:BaseUrl"];

        var httpClient = httpFactory.CreateClient("Maxio");
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" },
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default()
        };

        if (!string.IsNullOrWhiteSpace(baseUrlOverride))
        {
            options.Server.Production.Us.BaseUrl = baseUrlOverride;
        }
        else
        {
            options.Server.Production.Us.Site = subdomain;
        }

        _client = new MaxioAdvancedBillingClient(httpClient, options);
    }

    public async Task<List<PlanSummary>> GetPlansAsync(CancellationToken ct = default)
    {
        var results = await _client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: _familyHandle,
            dateField: null,
            filter: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            includeArchived: null,
            include: null,
            ct: ct);

        var list = new List<PlanSummary>();
        foreach (var item in results)
        {
            var p = item.Product;
            if (p == null) continue;
            list.Add(new PlanSummary
            {
                Handle = p.Handle ?? p.Id.ToString(),
                Name = p.Name ?? p.Handle ?? "Plan",
                Price = null,
                Interval = p.IntervalUnit?.ToString() ?? ""
            });
        }
        return list;
    }

    public async Task<SubscriptionResult> SubscribeAsync(string userEmail, string productHandle, CancellationToken ct = default)
    {
        var customers = await _client.Customers.ListCustomers(
            direction: null,
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            q: userEmail,
            page: 1,
            perPage: 10,
            ct: ct);

        int customerId = 0;
        string customerReference = userEmail;
        foreach (var c in customers)
        {
            var cust = c.Customer;
            if (cust != null && (cust.Email == userEmail || cust.Reference == userEmail || (cust.Id.HasValue && cust.Id.Value.ToString() == userEmail)))
            {
                customerId = cust.Id ?? 0;
                customerReference = cust.Reference ?? userEmail;
                break;
            }
        }

        if (customerId == 0)
        {
            var createResp = await _client.Customers.CreateCustomer(
                new MaxioAdvancedBilling.Models.CreateCustomerRequest
                {
                    Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                    {
                        Email = userEmail,
                        FirstName = "Shopper",
                        LastName = "User",
                        Reference = userEmail
                    }
                }, ct: ct);
            var newCust = createResp.Customer;
            if (newCust == null) throw new InvalidOperationException("Customer creation returned null");
            customerId = newCust.Id ?? 0;
            customerReference = newCust.Reference ?? userEmail;
        }

        // Idempotent check via customer subscriptions
        var subs = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
        foreach (var s in subs)
        {
            var sub = s.Subscription;
            if (sub == null) continue;
            var prodHandle = sub.Product?.Handle ?? sub.Product?.Id.ToString();
            if (prodHandle == productHandle && sub.State?.ToString() == "active")
            {
                return ToResult(sub, customerReference);
            }
        }

        var subResp = await _client.Subscriptions.CreateSubscription(
            new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerReference = customerReference
                }
            }, ct: ct);

        var created = subResp.Subscription;
        if (created == null) throw new InvalidOperationException("Subscription creation returned null");
        return ToResult(created, customerReference);
    }

    public async Task<List<SubscriptionResult>> GetMySubscriptionsAsync(string userEmail, CancellationToken ct = default)
    {
        var customers = await _client.Customers.ListCustomers(
            direction: null,
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            q: userEmail,
            page: 1,
            perPage: 10,
            ct: ct);

        int customerId = 0;
        foreach (var c in customers)
        {
            var cust = c.Customer;
            if (cust != null && (cust.Email == userEmail || cust.Reference == userEmail))
            {
                customerId = cust.Id ?? 0;
                break;
            }
        }
        if (customerId == 0) return new List<SubscriptionResult>();

        var subList = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
        var results = new List<SubscriptionResult>();
        foreach (var s in subList)
        {
            var sub = s.Subscription;
            if (sub != null) results.Add(ToResult(sub, userEmail));
        }
        return results;
    }

    private static SubscriptionResult ToResult(MaxioAdvancedBilling.Models.Subscription sub, string customerRef)
    {
        return new SubscriptionResult
        {
            Id = sub.Id ?? 0,
            ProductHandle = sub.Product?.Handle ?? sub.Product?.Id.ToString() ?? "",
            State = sub.State?.ToString() ?? "",
            NextBilling = sub.NextAssessmentAt,
            CustomerReference = customerRef
        };
    }
}
