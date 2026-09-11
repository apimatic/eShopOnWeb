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
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;

    public MaxioSubscriptionService(IOptions<MaxioSettings> settings)
    {
        _settings = settings.Value;

        var httpClient = new HttpClient();

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = _settings.ApiKey,
                Password = "x"
            }
        };

        if (!string.IsNullOrEmpty(_settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = _settings.BaseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = _settings.Subdomain;
        }

        options.Retry = RetryOptions.Default();

        _client = new MaxioAdvancedBillingClient(httpClient, options);
    }

    public async Task<(bool found, int customerId, string reference)> EnsureCustomerAsync(string reference, string email, string? firstName = null, string? lastName = null, CancellationToken ct = default)
    {
        try
        {
                var resp = await _client.Customers.ReadCustomerByReference(reference, ct);
                return (true, resp.Customer.Id ?? 0, reference);
        }
        catch (SdkException<RawError>)
        {
            // not found; create
        }

        try
        {
            var body = new MaxioAdvancedBilling.Models.CreateCustomerRequest
            {
                Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                {
                    Reference = reference,
                    Email = email,
                    FirstName = firstName,
                    LastName = lastName,
                    Organization = reference
                }
            };
            var resp = await _client.Customers.CreateCustomer(body, ct);
            return (true, resp.Customer.Id ?? 0, reference);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // If reference conflict (already exists), try read again
            if (ex.Error.TryGetCustomerErrorResponse1(out var errResp))
            {
                // may contain validation errors; attempt re-read
            }
            try
            {
                var resp = await _client.Customers.ReadCustomerByReference(reference, ct);
            return (true, resp.Customer.Id ?? 0, reference);
            }
            catch
            {
                throw;
            }
        }
    }

    public async Task<int> FindOrCreateSubscriptionAsync(string customerRef, string planHandle, CancellationToken ct = default)
    {
        var subRef = $"{customerRef}-{planHandle}";
        try
        {
            var resp = await _client.Subscriptions.FindSubscription(subRef, ct);
            return resp.Subscription.Id ?? 0;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                // not found; create
            }
            else if (ex.Error.TryGetRawError(out _))
            {
                // not found or other
            }
        }
        catch (SdkException<RawError>)
        {
            // not found
        }

        var customerResp = await _client.Customers.ReadCustomerByReference(customerRef, ct);
        var customerId = customerResp.Customer.Id ?? 0;

        var body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
        {
            Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerReference = customerRef,
                Reference = subRef
            }
        };

        var subResp = await _client.Subscriptions.CreateSubscription(body, ct);
        return subResp.Subscription.Id ?? 0;
    }

    public async Task<SubscriptionResponse[]> ListSubscriptionsForCustomerAsync(string customerRef, CancellationToken ct = default)
    {
        var customerResp = await _client.Customers.ReadCustomerByReference(customerRef, ct);
        int customerId = customerResp.Customer.Id ?? 0;
        var all = await _client.Subscriptions.ListSubscriptions(
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
            metadata: null,
            direction: null,
            sort: null,
            include: null,
            page: 1,
            perPage: 50,
            ct: ct);
        var results = new System.Collections.Generic.List<SubscriptionResponse>();
        foreach (var item in all)
        {
            var sub = item.Subscription;
            if (sub != null && ((sub.Customer?.Reference == customerRef) || (sub.Customer?.Id == customerId)))
            {
                results.Add(item);
            }
        }
        return results.ToArray();
    }

    public async Task<IReadOnlyList<ProductResponse>> ListPlansAsync(CancellationToken ct = default)
    {
        var products = await _client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: _settings.ProductFamilyHandle,
            dateField: null,
            filter: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            includeArchived: null,
            include: null,
            page: 1,
            perPage: 20,
            ct: ct);
        return products;
    }
}
