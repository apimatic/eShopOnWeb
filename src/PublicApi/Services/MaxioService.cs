using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Services;
public class MaxioService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioService> _logger;
    private readonly SubscriptionMappingService _mapper;

    public MaxioService(IConfiguration config, ILogger<MaxioService> logger, SubscriptionMappingService mapper)
    {
        _logger = logger;
        _mapper = mapper;
        var apiKey = config["Maxio:ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey missing");
        var subdomain = config["Maxio:Subdomain"] ?? throw new InvalidOperationException("Maxio:Subdomain missing");
        _productFamilyHandle = config["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" }
        };

        var baseUrl = config["Maxio:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us;
            options.Server = new MaxioAdvancedBilling.ServerOptions();
            // The SDK maps server options via template; setting BaseUrl directly requires
            // configuring the server environment override. Given contract notes, we construct
            // environment with subdomain and rely on default templated URL when no BaseUrl.
            // If BaseUrl is provided, we pass it through a custom HttpClient base address instead.
        }
        else
        {
            options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us;
        }

        var httpClient = new HttpClient();
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
        }
        else
        {
            httpClient.BaseAddress = new Uri($"https://{subdomain}.chargify.com/");
        }
        _client = new MaxioAdvancedBillingClient(httpClient, options);
    }

    public async Task<object> GetPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var family = await _client.Products.ReadProductByHandle(_productFamilyHandle, ct);
            var product = family.Product;
            // List price points for family/product id if available
            var pricePoints = await _client.ProductPricePoints.ListProductPricePoints(
                MaxioAdvancedBilling.Models.AnyOf.ProductIdModel.String(_productFamilyHandle),
                currencyPrices: null,
                filterType: null,
                archived: null,
                ct: ct);
            return new { Family = product, PricePoints = pricePoints };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get plans");
            throw;
        }
    }

    public async Task<object> SubscribeAsync(string userId, string planHandle, CancellationToken ct = default)
    {
        // Idempotent customer creation
        int customerId;
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(userId, ct);
            customerId = (int)(existing.Customer.Id ?? 0);
            _mapper.SetMapping(userId, customerId.ToString(), "");
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var createReq = new MaxioAdvancedBilling.Models.CreateCustomerRequest
            {
                Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                {
                    FirstName = "User",
                    LastName = userId,
                    Email = $"{userId}@eshop.local",
                    Reference = userId
                }
            };
            var created = await _client.Customers.CreateCustomer(createReq, ct);
            customerId = (int)(created.Customer.Id ?? 0);
            _mapper.SetMapping(userId, customerId.ToString(), "");
        }

        // Idempotent subscription creation via FindSubscription by reference
        try
        {
            var found = await _client.Subscriptions.FindSubscription(userId, ct);
            var sub = found.Subscription;
            _mapper.SetMapping(userId, customerId.ToString(), sub.Id?.ToString() ?? "");
            return sub;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var subReq = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerReference = userId
                }
            };
            var created = await _client.Subscriptions.CreateSubscription(subReq, ct);
            var sub = created.Subscription;
            _mapper.SetMapping(userId, customerId.ToString(), sub.Id?.ToString() ?? "");
            return sub;
        }
    }

    public async Task<object> GetMySubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        var customerIdStr = _mapper.GetCustomerId(userId);
            if (!string.IsNullOrEmpty(customerIdStr) && int.TryParse(customerIdStr, out var customerIdInt))
        {
            return await _client.Customers.ListCustomerSubscriptions(customerIdInt, ct);
        }
        // Fallback: resolve customer by reference
        try
        {
            var cust = await _client.Customers.ReadCustomerByReference(userId, ct);
            var fallbackCustomerId = (int)(cust.Customer.Id ?? 0);
            if (fallbackCustomerId > 0)
            {
                _mapper.SetMapping(userId, fallbackCustomerId.ToString(), _mapper.GetSubscriptionId(userId) ?? "");
                return await _client.Customers.ListCustomerSubscriptions(fallbackCustomerId, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer lookup failed for {UserId}", userId);
        }
        return new System.Collections.Generic.List<object>();
    }
}
