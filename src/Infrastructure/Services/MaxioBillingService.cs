using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Servers;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioBillingService : IMaxioBillingService
{
    private readonly MaxioAdvancedBillingClient _client;

    public MaxioBillingService(IConfiguration cfg)
    {
        var apiKey = cfg["Maxio:ApiKey"] ?? Environment.GetEnvironmentVariable("MAXIO_API_KEY");
        var subdomain = cfg["Maxio:Subdomain"] ?? Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");
        var baseUrlOverride = cfg["Maxio:BaseUrl"];

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = apiKey ?? "", Password = "x" },
            Environment = ServerEnvironment.Us,
        };

        if (!string.IsNullOrWhiteSpace(subdomain))
        {
            options.Server.Production.Us.Site = subdomain;
        }

        if (!string.IsNullOrWhiteSpace(baseUrlOverride))
        {
            options.Server.Production.Us.BaseUrl = baseUrlOverride;
        }

        _client = new MaxioAdvancedBillingClient(new HttpClient(), options);
    }

    public async Task<SubscriptionPlanDto> GetPlanByHandleAsync(string handle, CancellationToken ct = default)
    {
        try
        {
            var resp = await _client.Products.ReadProductByHandle(handle, ct);
            var p = resp.Product;
            return new SubscriptionPlanDto(p.Handle ?? handle, p.Id ?? 0, p.Name ?? handle, 0m);
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Plan not found: {handle}", ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        var list = new List<SubscriptionPlanDto>();
        foreach (var h in new[] { "eshop-pro", "basic-plan" })
        {
            try { var r = await GetPlanByHandleAsync(h, ct); list.Add(r); } catch { }
        }
        return list;
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(string userReference, string planHandle, CancellationToken ct = default)
    {
        // Idempotent customer ensure
        int customerId = await EnsureCustomerAsync(userReference, ct);

        var req = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                CustomerReference = userReference,
            }
        };

        var resp = await _client.Subscriptions.CreateSubscription(req, ct);
        var sub = resp.Subscription;
        return new SubscriptionDto(
            sub.Id ?? 0,
            sub.State?.ToString() ?? "unknown",
            sub.Product?.Handle ?? planHandle,
            299m,
            ""
        );
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userReference, CancellationToken ct = default)
    {
        var customer = await FindCustomerAsync(userReference, ct);
        if (customer?.Customer == null) return new List<SubscriptionDto>();

        var list = await _client.Customers.ListCustomerSubscriptions(customer.Customer.Id ?? 0, ct);
        return list.Select(s => new SubscriptionDto(
            s.Subscription?.Id ?? 0,
            s.Subscription?.State?.ToString() ?? "unknown",
            s.Subscription?.Product?.Handle ?? "",
            299m,
            ""
        )).ToList();
    }

    private async Task<int> EnsureCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(reference, ct);
            return existing.Customer?.Id ?? 0;
        }
        catch (SdkException<RawError>)
        {
            // Not found -> create
        }

        var createReq = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                Reference = reference,
                Email = reference,
                FirstName = "Customer",
                LastName = reference,
            }
        };
        var created = await _client.Customers.CreateCustomer(createReq, ct);
        return created.Customer?.Id ?? 0;
    }

    private async Task<CustomerResponse?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            return await _client.Customers.ReadCustomerByReference(reference, ct);
        }
        catch (SdkException<RawError>)
        {
            return null;
        }
    }
}
