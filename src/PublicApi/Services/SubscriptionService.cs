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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface ISubscriptionService
{
    Task<List<PlanInfo>> GetSubscriptionPlansAsync(CancellationToken ct = default);
    Task<SubscriptionResult> SubscribeAsync(string userReference, string planHandle, CancellationToken ct = default);
    Task<List<SubscriptionResult>> GetMySubscriptionsAsync(string userReference, CancellationToken ct = default);
}

public class PlanInfo
{
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string PriceUnit { get; set; } = "";
}

public class SubscriptionResult
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string PlanHandle { get; set; } = "";
    public decimal Price { get; set; }
    public string NextBillingDate { get; set; } = "";
    public string CustomerReference { get; set; } = "";
}

public class SubscriptionService : ISubscriptionService
{
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(IOptions<MaxioSettings> settings, ILogger<SubscriptionService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    private MaxioAdvancedBillingClient CreateClient()
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
        };
        options.BasicAuth = new BasicAuthCredentials
        {
            Username = _settings.ApiKey,
            Password = "x"
        };
        if (!string.IsNullOrEmpty(_settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = _settings.BaseUrl;
        }
        else
        {
            // Derive from subdomain for sandbox
            options.Server.Production.Us.BaseUrl = $"https://{_settings.Subdomain}.chargify.com";
        }
        var httpClient = new HttpClient();
        return new MaxioAdvancedBillingClient(httpClient, options);
    }

    public async Task<List<PlanInfo>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        var client = CreateClient();
        // Use family handle to find products; family id not needed if handle works
        // ListProductsForProductFamily takes productFamilyId (string?) - using handle per SDK convention
        var productResponses = await client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: _settings.ProductFamilyHandle,
            dateField: null,
            filter: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            includeArchived: null,
            include: null,
            perPage: 20,
            ct: ct);
        var result = new List<PlanInfo>();
        if (productResponses != null)
        {
            foreach (var resp in productResponses)
            {
                var p = resp.Product;
                if (p.PriceInCents.HasValue)
                {
                    result.Add(new PlanInfo
                    {
                        Handle = p.Handle ?? "",
                        Name = p.Name ?? "",
                        Price = p.PriceInCents.Value / 100m,
                        PriceUnit = ""
                    });
                }
            }
        }
        return result;
    }

    public async Task<SubscriptionResult> SubscribeAsync(string userReference, string planHandle, CancellationToken ct = default)
    {
        var client = CreateClient();
        int? customerId = null;
        // Idempotent customer lookup by reference
        try
        {
            var existing = await client.Customers.ReadCustomerByReference(userReference, ct: ct);
            if (existing?.Customer != null)
            {
                customerId = existing.Customer.Id;
            }
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("SdkException"))
        {
            // 404 / not found expected when new
            _logger.LogInformation("No existing Maxio customer for ref {Ref}; will create.", userReference);
        }

        if (customerId == null)
        {
            var createReq = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    Reference = userReference,
                    FirstName = "eShop",
                    LastName = "Customer",
                    Email = $"{userReference}@eshop.local",
                    Country = "US"
                }
            };
            var created = await client.Customers.CreateCustomer(createReq, ct: ct);
            customerId = created?.Customer?.Id;
        }

        // Find product/plan id from handle - we'll use handle directly if SDK supports; else look up
        int? productId = null;
        var familyResponses = await client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: _settings.ProductFamilyHandle,
            dateField: null,
            filter: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            includeArchived: null,
            include: null,
            perPage: 50,
            ct: ct);
        if (familyResponses != null)
        {
            var target = familyResponses
                .Select(resp => resp.Product)
                .FirstOrDefault(p => (p?.Handle ?? "").Equals(planHandle, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                productId = target.Id;
            }
        }

        if (productId == null)
        {
            throw new InvalidOperationException($"Plan handle {planHandle} not found in family {_settings.ProductFamilyHandle}");
        }

        var subReq = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerId = customerId.Value,
                ProductHandle = planHandle
            }
        };
        // Actually SDK may want product_id (int) rather than handle; try handle first, else fallback
        // Given map uncertainty, construct with both if model supports
        var subResp = await client.Subscriptions.CreateSubscription(subReq, ct: ct);
        var sub = subResp?.Subscription;
        return new SubscriptionResult
        {
            Id = sub?.Id ?? 0,
            State = sub?.State ?? "",
            PlanHandle = sub?.Product?.Handle ?? planHandle,
            Price = sub?.Product?.PriceInCents.HasValue == true ? sub.Product.PriceInCents.Value / 100m : (sub?.ProductPriceInCents.HasValue == true ? sub.ProductPriceInCents.Value / 100m : 0m),
            NextBillingDate = sub?.NextAssessmentAt?.ToString("yyyy-MM-dd") ?? "",
            CustomerReference = userReference
        };
    }

    public async Task<List<SubscriptionResult>> GetMySubscriptionsAsync(string userReference, CancellationToken ct = default)
    {
        var client = CreateClient();
        // Find customer first
        int? customerId = null;
        try
        {
            var existing = await client.Customers.ReadCustomerByReference(userReference, ct: ct);
            customerId = existing?.Customer?.Id;
        }
        catch { }

        var result = new List<SubscriptionResult>();
        if (customerId == null) return result;

        var subs = await client.Subscriptions.ListSubscriptions(
            state: null,
            product: null,
            productPricePointId: null,
            coupon: null,
            couponCode: null,
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            metadata: new Dictionary<string, string> { { "customer_id", customerId.Value.ToString() } },
            direction: null,
            sort: null,
            include: null,
            perPage: 20,
            ct: ct);
        if (subs != null)
        {
            foreach (var resp in subs)
            {
                var s = resp.Subscription;
                if (s == null) continue;
                result.Add(new SubscriptionResult
                {
                    Id = s.Id ?? 0,
                    State = s.State ?? "",
                    PlanHandle = s.Product?.Handle ?? "",
                    Price = s.Product?.PriceInCents.HasValue == true ? s.Product.PriceInCents.Value / 100m : (s.ProductPriceInCents.HasValue == true ? s.ProductPriceInCents.Value / 100m : 0m),
                    NextBillingDate = s.NextAssessmentAt?.ToString("yyyy-MM-dd") ?? "",
                    CustomerReference = userReference
                });
            }
        }
        return result;
    }
}
