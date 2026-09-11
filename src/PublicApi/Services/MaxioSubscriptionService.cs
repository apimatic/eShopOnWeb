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

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<ProductResponse>> ListPlansAsync(CancellationToken ct = default);
    Task<CustomerResponse?> FindCustomerByEmailAsync(string email, CancellationToken ct = default);
    Task<CustomerResponse> CreateCustomerAsync(string email, string firstName, string lastName, CancellationToken ct = default);
    Task<SubscriptionResponse> CreateSubscriptionAsync(int customerId, int productId, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;

    public MaxioSubscriptionService(MaxioSettings settings)
    {
        _settings = settings;
        var http = new HttpClient();
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Default(),
        };

        options.BasicAuth = new BasicAuthCredentials
        {
            Username = settings.ApiKey,
            Password = "x"
        };

        if (!string.IsNullOrEmpty(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }
        else if (!string.IsNullOrEmpty(settings.Subdomain))
        {
            options.Server.Production.Us.Site = settings.Subdomain;
        }

        _client = new MaxioAdvancedBillingClient(http, options);
    }

    public async Task<IReadOnlyList<ProductResponse>> ListPlansAsync(CancellationToken ct = default)
    {
        return await _client.ProductFamilies.ListProductsForProductFamily(
            _settings.ProductFamilyHandle,
            dateField: null, filter: null, startDate: null, endDate: null,
            startDatetime: null, endDatetime: null, includeArchived: null,
            include: null, page: 1, perPage: 50, ct: ct);
    }

    public async Task<CustomerResponse?> FindCustomerByEmailAsync(string email, CancellationToken ct = default)
    {
        var results = await _client.Customers.ListCustomers(
            direction: null, dateField: null, startDate: null, endDate: null,
            startDatetime: null, endDatetime: null,
            q: email, page: 1, perPage: 50, ct: ct);
        return results.FirstOrDefault();
    }

    public async Task<CustomerResponse> CreateCustomerAsync(string email, string firstName, string lastName, CancellationToken ct = default)
    {
        var req = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                Reference = email
            }
        };
        return await _client.Customers.CreateCustomer(req, ct);
    }

    public async Task<SubscriptionResponse> CreateSubscriptionAsync(int customerId, int productId, CancellationToken ct = default)
    {
        var req = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerId = customerId,
                ProductId = productId
            }
        };
        return await _client.Subscriptions.CreateSubscription(req, ct);
    }

    public async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        return await _client.Customers.ListCustomerSubscriptions(customerId, ct);
    }
}
