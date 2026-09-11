using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

public class MaxioBillingService
{
    private readonly MaxioSettings _settings;
    private readonly HttpClient _httpClient;

    public MaxioBillingService(IOptions<MaxioSettings> settings, IHttpClientFactory httpClientFactory)
    {
        _settings = settings.Value;
        _httpClient = httpClientFactory.CreateClient();
    }

    private MaxioAdvancedBillingClient CreateClient()
    {
        var env = _settings.Subdomain.Contains("eu", StringComparison.OrdinalIgnoreCase)
            ? ServerEnvironment.Eu : ServerEnvironment.Us;

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = _settings.ApiKey,
                Password = "x"
            },
            Environment = env,
            Server = new ServerOptions
            {
                Production = new ProductionOptions
                {
                    Us = new ProductionOptions.UsOptions
                    {
                        Site = _settings.Subdomain,
                        BaseUrl = _settings.BaseUrl
                    },
                    Eu = new ProductionOptions.EuOptions
                    {
                        Site = _settings.Subdomain,
                        BaseUrl = _settings.BaseUrl
                    }
                }
            },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 2,
                Timeout = TimeSpan.FromSeconds(30)
            }
        };

        return new MaxioAdvancedBillingClient(_httpClient, options);
    }

    public async Task<List<PlanInfo>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        var client = CreateClient();
        var familyHandle = _settings.ProductFamilyHandle;

        // ListProductsFilter has no Family property; family filtering not supported via filter (map: ListProductsFilter only Ids/PrepaidProductPricePoint/UseSiteExchangeRate)
        // ListProducts: 8 nullable params without defaults must be passed explicitly (map: Products.md)
        var response = await client.Products.ListProducts(
            dateField: null,
            filter: null,
            endDate: null,
            endDatetime: null,
            startDate: null,
            startDatetime: null,
            includeArchived: null,
            include: null,
            page: 1,
            perPage: 20,
            ct: ct);

        var plans = new List<PlanInfo>();
        foreach (var item in response)
        {
            var p = item.Product;
            if (p == null) continue;
            plans.Add(new PlanInfo
            {
                Handle = p.Handle ?? p.Id.ToString(),
                Name = p.Name ?? p.Handle ?? "Plan",
                Price = 0, // SDK Product model has no direct Price; price lives on price-point objects (map: Product.cs)
                FamilyHandle = familyHandle
            });
        }
        return plans;
    }

    public async Task<string> EnsureCustomerAsync(string userId, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        var client = CreateClient();
        try
        {
            var existing = await client.Customers.ReadCustomerByReference(reference: userId, ct: ct);
            return existing.Customer?.Id?.ToString() ?? userId;
        }
        catch (SdkException<RawError>)
        {
            // not found; fall through to create
        }

        var createBody = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = userId
            }
        };

        var result = await client.Customers.CreateCustomer(body: createBody, ct: ct);
        return result.Customer?.Id?.ToString() ?? userId;
    }

    public async Task<SubscriptionInfo> SubscribeAsync(string userId, string productHandle, CancellationToken ct = default)
    {
        var client = CreateClient();

        // CreateSubscriptionRequest embeds CreateSubscription (not Subscription); property names ProductHandle/CustomerReference/Reference are correct (map: CreateSubscription.cs)
        var subRequest = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = userId,
                Reference = $"sub-{userId}-{productHandle}"
            }
        };

        var result = await client.Subscriptions.CreateSubscription(body: subRequest, ct: ct);
        var sub = result.Subscription;
        // Subscription model has no flat Price/NextBillingAt/ProductHandle/CustomerReference (map: Subscription.cs); read nested Product/Customer
        return new SubscriptionInfo
        {
            Id = sub?.Id?.ToString() ?? "",
            State = sub?.State?.ToString() ?? "",
            ProductHandle = sub?.Product?.Handle ?? productHandle,
            Price = sub?.ProductPriceInCents == null ? 0 : sub.ProductPriceInCents.Value / 100m,
            NextBillingDate = sub?.NextAssessmentAt,
            CustomerReference = sub?.Customer?.Reference ?? userId
        };
    }

    public async Task<List<SubscriptionInfo>> ListMySubscriptionsAsync(string userReference, CancellationToken ct = default)
    {
        var client = CreateClient();
        var customer = await client.Customers.ReadCustomerByReference(reference: userReference, ct: ct);
        int customerId = customer.Customer?.Id ?? 0;
        if (customerId == 0) return new List<SubscriptionInfo>();

        // ListCustomerSubscriptions has only customerId + ct (map: Customers.md); no pagination params
        var response = await client.Customers.ListCustomerSubscriptions(
            customerId: customerId,
            ct: ct);

        var list = new List<SubscriptionInfo>();
        foreach (var item in response)
        {
            var s = item.Subscription;
            if (s == null) continue;
            // Subscription response embeds Subscription model (map: SubscriptionResponse/Subscription.cs)
            list.Add(new SubscriptionInfo
            {
                Id = s.Id?.ToString() ?? "",
                State = s.State?.ToString() ?? "",
                ProductHandle = s.Product?.Handle ?? "",
                Price = s.ProductPriceInCents == null ? 0 : s.ProductPriceInCents.Value / 100m,
                NextBillingDate = s.NextAssessmentAt,
                CustomerReference = s.Customer?.Reference ?? userReference
            });
        }
        return list;
    }
}

public record PlanInfo
{
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string FamilyHandle { get; set; } = "";
}

public record SubscriptionInfo
{
    public string Id { get; set; } = "";
    public string State { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public string CustomerReference { get; set; } = "";
}
