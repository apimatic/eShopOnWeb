using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

public interface ISubscriptionService
{
    Task<object?> GetPlansAsync(CancellationToken ct = default);
    Task<object?> SubscribeAsync(string userName, string productHandle, CancellationToken ct = default);
    Task<object?> GetMySubscriptionsAsync(string userName, CancellationToken ct = default);
}

public class MaxioSubscriptionService : ISubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;

    public MaxioSubscriptionService(IConfiguration config)
    {
        var apiKey = config["Maxio:ApiKey"]!;
        var subdomain = config["Maxio:Subdomain"]!;
        var baseUrl = config["Maxio:BaseUrl"];

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" },
        };
        if (!string.IsNullOrWhiteSpace(baseUrl))
            options.Server.Production.Us.BaseUrl = baseUrl;
        else
            options.Server.Production.Us.Site = subdomain;

        _client = new MaxioAdvancedBillingClient(new System.Net.Http.HttpClient(), options);
    }

    public async Task<object?> GetPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: "eshop-subscribe",
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: null,
                perPage: null,
                ct: ct);
            return products.Select(p => new { handle = p.Product?.Handle, name = p.Product?.Name, priceInCents = p.Product?.PriceInCents }).ToList();
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    public async Task<object?> SubscribeAsync(string userName, string productHandle, CancellationToken ct = default)
    {
        try
        {
            var email = userName;
            var customers = await _client.Customers.ListCustomers(
                direction: null, dateField: null, startDate: null, endDate: null,
                startDatetime: null, endDatetime: null, q: email, ct: ct);
            var existing = customers.FirstOrDefault(c => string.Equals(c.Customer?.Email, email, StringComparison.OrdinalIgnoreCase));
            int customerId;
            if (existing != null)
            {
                customerId = existing.Customer!.Id.Value;
            }
            else
            {
                var createReq = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = "Shopper",
                        LastName = "User",
                        Email = email,
                    }
                };
                var created = await _client.Customers.CreateCustomer(createReq, ct);
                customerId = created.Customer!.Id.Value;
            }

            var subReq = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle,
                }
            };
            var sub = await _client.Subscriptions.CreateSubscription(subReq, ct);
            return new
            {
                subscriptionId = sub.Subscription?.Id,
                productHandle = sub.Subscription?.Product?.Handle,
                state = sub.Subscription?.State,
                customerId = customerId,
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    public async Task<object?> GetMySubscriptionsAsync(string userName, CancellationToken ct = default)
    {
        try
        {
            var email = userName;
            var customers = await _client.Customers.ListCustomers(
                direction: null, dateField: null, startDate: null, endDate: null,
                startDatetime: null, endDatetime: null, q: email, ct: ct);
            var customer = customers.FirstOrDefault(c => string.Equals(c.Customer?.Email, email, StringComparison.OrdinalIgnoreCase));
            if (customer == null) return new object[] { };
            var subs = await _client.Customers.ListCustomerSubscriptions(customer.Customer!.Id.Value, ct);
            return subs.Select(s => new
            {
                id = s.Subscription?.Id,
                productHandle = s.Subscription?.Product?.Handle,
                state = s.Subscription?.State,
            }).ToList();
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }
}
