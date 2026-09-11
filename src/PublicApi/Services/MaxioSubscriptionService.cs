using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Servers;
using MaxioAdvancedBilling.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

public sealed class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionService(IConfiguration config, ILogger<MaxioSubscriptionService> logger)
    {
        _logger = logger;
        var apiKey = config["Maxio:ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey missing");
        var subdomain = config["Maxio:Subdomain"] ?? throw new InvalidOperationException("Maxio:Subdomain missing");
        _productFamilyHandle = config["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";
        var baseUrl = config["Maxio:BaseUrl"];

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" }
        };
        if (!string.IsNullOrEmpty(baseUrl))
        {
            options.Server = new ServerOptions();
            options.Server.Production.Us.BaseUrl = baseUrl;
            options.Server.Production.Eu.BaseUrl = baseUrl;
        }
        else
        {
            // Derive from subdomain via server options if needed; default template uses site param.
            // Since SDK default uses site parameter, but we don't have direct site param on options,
            // using base-URL override with known sandbox URL is acceptable per brief.
            options.Server = new ServerOptions();
            options.Server.Production.Us.BaseUrl = $"https://{subdomain}.chargify.com";
        }

        var httpClient = new System.Net.Http.HttpClient();
        _client = new MaxioAdvancedBillingClient(httpClient, options);
    }

    public async Task<CustomerResponse> GetOrCreateCustomerAsync(string email, string reference, CancellationToken ct = default)
    {
        try
        {
            var list = await _client.Customers.ListCustomers(direction: null, dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, q: email, page: 1, perPage: 10, ct: ct);
            var existing = list.FirstOrDefault(c => c.Customer?.Email == email || c.Customer?.Reference == reference);
            if (existing != null)
                return existing;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning(ex, "Customer list failed");
        }

        try
        {
            return await _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    Email = email,
                    Reference = reference,
                    FirstName = "Shopper",
                    LastName = "User"
                }
            }, ct: ct);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var err1))
                _logger.LogError("Create customer 422: {Msg}", err1?.ToString());
            else if (ex.Error.TryGetRawError(out var raw))
                _logger.LogError("Create customer error: {Status} {Body}", raw.StatusCode, raw.ReadAsString());
            throw;
        }
    }

    public async Task<IReadOnlyList<ProductResponse>> GetPlanProductsAsync(CancellationToken ct = default)
    {
        var family = await _client.ProductFamilies.ReadProductFamily(3023074, ct: ct);
        var products = await _client.ProductFamilies.ListProductsForProductFamily("3023074", dateField: null, filter: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, includeArchived: null, include: null, page: 1, perPage: 20, ct: ct);
        return products;
    }

    public async Task<IReadOnlyList<SubscriptionResponse>> ListSubscriptionsAsync(int customerId, int? productId = null, CancellationToken ct = default)
    {
        return await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
    }

    public async Task<SubscriptionResponse> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken ct = default)
    {
        try
        {
            return await _client.Subscriptions.CreateSubscription(request, ct: ct);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var err))
                _logger.LogError("Create subscription 422: {Msg}", err?.ToString());
            else if (ex.Error.TryGetRawError(out var raw))
                _logger.LogError("Create subscription error: {Status} {Body}", raw.StatusCode, raw.ReadAsString());
            throw;
        }
    }

    public MaxioAdvancedBillingClient Client => _client;
}
