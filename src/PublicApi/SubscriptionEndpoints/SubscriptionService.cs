using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default);
    Task<SubscriptionResult> SubscribeAsync(string customerReference, string productHandle, CancellationToken ct = default);
    Task<IReadOnlyList<MySubscriptionDto>> GetMySubscriptionsAsync(string customerReference, CancellationToken ct = default);
}

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = "";
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public string Period { get; set; } = "month";
}

public class SubscriptionResult
{
    public bool Success { get; set; }
    public string CustomerReference { get; set; } = "";
    public int CustomerId { get; set; }
    public int SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public DateTime? NextBillingDate { get; set; }
    public string PlanHandle { get; set; } = "";
    public string Message { get; set; } = "";
}

public class MySubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = "";
    public string State { get; set; } = "";
    public DateTime? NextBillingDate { get; set; }
    public decimal Price { get; set; }
}

public class SubscriptionService : ISubscriptionService
{
    private readonly IConfiguration _config;
    private readonly HttpClient _http;

    public SubscriptionService(IConfiguration config, IHttpClientFactory httpFactory)
    {
        _config = config;
        _http = httpFactory.CreateClient();
    }

    private MaxioAdvancedBillingClient CreateClient()
    {
        var apiKey = _config["Maxio:ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey missing");
        var subdomain = _config["Maxio:Subdomain"] ?? throw new InvalidOperationException("Maxio:Subdomain missing");
        var baseUrl = _config["Maxio:BaseUrl"];

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
        };
        options.BasicAuth = new BasicAuthCredentials
        {
            Username = apiKey,
            Password = "x"
        };
        options.Server.Production.Us.Site = subdomain;
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            options.Server.Production.Us.BaseUrl = baseUrl;
        }

        return new MaxioAdvancedBillingClient(_http, options);
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default)
    {
        var results = new List<SubscriptionPlanDto>();
        var client = CreateClient();

        var handles = new[] { "eshop-pro", "basic-plan" };
        foreach (var h in handles)
        {
            try
            {
                var resp = await client.Products.ReadProductByHandle(h, ct: ct);
                var prod = resp.Product;
                if (prod != null)
                {
                    results.Add(new SubscriptionPlanDto
                    {
                        Handle = h,
                        Id = prod.Id ?? 0,
                        Name = prod.Name ?? h,
                        Price = (prod.PriceInCents ?? 0) / 100m,
                        Currency = "USD",
                        Period = "month"
                    });
                }
            }
            catch { /* skip */ }
        }
        return results;
    }

    public async Task<SubscriptionResult> SubscribeAsync(string customerReference, string productHandle, CancellationToken ct = default)
    {
        var result = new SubscriptionResult { CustomerReference = customerReference, PlanHandle = productHandle };
        var client = CreateClient();

        int customerId = 0;
        try
        {
            var customerResp = await client.Customers.ReadCustomerByReference(customerReference, ct: ct);
            customerId = customerResp.Customer?.Id ?? 0;
        }
        catch
        {
            try
            {
                var inner = new CreateCustomer
                {
                    FirstName = "User",
                    LastName = "Customer",
                    Email = customerReference.Contains("@") ? customerReference : $"{customerReference}@example.com",
                    Reference = customerReference
                };
                var createReq = new CreateCustomerRequest { Customer = inner };
                var createResp = await client.Customers.CreateCustomer(createReq, ct: ct);
                customerId = createResp.Customer?.Id ?? 0;
            }
            catch (Exception ex)
            {
                try
                {
                    var retryResp = await client.Customers.ReadCustomerByReference(customerReference, ct: ct);
                    customerId = retryResp.Customer?.Id ?? 0;
                }
                catch
                {
                    result.Success = false;
                    result.Message = $"Customer creation/lookup failed: {ex.Message}";
                    return result;
                }
            }
        }

        if (customerId == 0)
        {
            result.Success = false;
            result.Message = "Could not resolve customer.";
            return result;
        }

        result.CustomerId = customerId;

        try
        {
            var inner = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = $"sub-{customerReference}-{productHandle}"
            };
            var subReq = new CreateSubscriptionRequest { Subscription = inner };
            var subResp = await client.Subscriptions.CreateSubscription(subReq, ct: ct);
            var sub = subResp.Subscription;
            if (sub != null)
            {
                result.Success = true;
                result.SubscriptionId = sub.Id ?? 0;
                result.State = sub.State != null ? sub.State.ToString() : "";
                result.NextBillingDate = sub.NextAssessmentAt.HasValue ? (DateTime?)sub.NextAssessmentAt.Value.DateTime : (sub.CurrentPeriodEndsAt.HasValue ? (DateTime?)sub.CurrentPeriodEndsAt.Value.DateTime : null);
                result.Message = "Subscribed successfully.";
            }
            else
            {
                result.Success = false;
                result.Message = "Subscription response missing.";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Subscription creation failed: {ex.Message}";
        }
        return result;
    }

    public async Task<IReadOnlyList<MySubscriptionDto>> GetMySubscriptionsAsync(string customerReference, CancellationToken ct = default)
    {
        var results = new List<MySubscriptionDto>();
        var client = CreateClient();
        try
        {
            var custResp = await client.Customers.ReadCustomerByReference(customerReference, ct: ct);
            var cust = custResp.Customer;
            if (cust == null) return results;
            var subs = await client.Customers.ListCustomerSubscriptions(cust.Id ?? 0, ct: ct);
            foreach (var s in subs)
            {
                var sub = s.Subscription;
                if (sub != null)
                {
                    results.Add(new MySubscriptionDto
                    {
                        SubscriptionId = sub.Id ?? 0,
                        ProductHandle = sub.Product?.Handle ?? sub.NextProductHandle ?? "",
                        State = sub.State != null ? sub.State.ToString() : "",
                        NextBillingDate = sub.NextAssessmentAt.HasValue ? (DateTime?)sub.NextAssessmentAt.Value.DateTime : (sub.CurrentPeriodEndsAt.HasValue ? (DateTime?)sub.CurrentPeriodEndsAt.Value.DateTime : null),
                        Price = (sub.ProductPriceInCents ?? 0) / 100m
                    });
                }
            }
        }
        catch { /* return empty */ }
        return results;
    }
}
