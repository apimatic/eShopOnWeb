using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

public interface IMaxioBillingService
{
    Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken ct = default);
    Task<SubscriptionEnrollmentResult> SubscribeAsync(string userReference, string planHandle, CancellationToken ct = default);
    Task<IReadOnlyList<MySubscriptionInfo>> GetMySubscriptionsAsync(string userReference, CancellationToken ct = default);
}

public class SubscriptionPlanInfo
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal? PriceInCents { get; set; }
}

public class SubscriptionEnrollmentResult
{
    public int CustomerId { get; set; }
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string NextBillingDate { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public class MySubscriptionInfo
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string NextBillingDate { get; set; } = string.Empty;
}

public class MaxioBillingService : IMaxioBillingService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _familyHandle;

    public MaxioBillingService(MaxioAdvancedBillingClient client, IConfiguration config)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _familyHandle = config["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";
    }

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken ct = default)
    {
        // Read catalog dynamically from the seeded family
        var products = await _client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: _familyHandle,
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

        var result = new List<SubscriptionPlanInfo>();
        foreach (var item in products)
        {
            var p = item.Product;
            if (p == null) continue;
            decimal? price = null;
            result.Add(new SubscriptionPlanInfo
            {
                Handle = p.Handle ?? string.Empty,
                Name = p.Name ?? string.Empty,
                PriceInCents = price
            });
        }
        return result;
    }

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(string userReference, string planHandle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userReference))
            throw new ArgumentException("User reference required", nameof(userReference));
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new ArgumentException("Plan handle required", nameof(planHandle));

        // Idempotent customer find / create
        int customerId = await EnsureCustomerAsync(userReference, ct);

        // Build subscription request using handle (avoids numeric IDs)
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerReference = userReference,
                Reference = $"sub-{userReference}-{planHandle}",
                // No trial, no setup fee — rely on seeded plan defaults
            }
        };

        var response = await _client.Subscriptions.CreateSubscription(request, ct);
        var sub = response.Subscription;
        return new SubscriptionEnrollmentResult
        {
            CustomerId = customerId,
            SubscriptionId = sub.Id ?? 0,
            State = sub.State?.ToString() ?? string.Empty,
            NextBillingDate = sub.CurrentPeriodEndsAt?.ToString("yyyy-MM-dd") ?? string.Empty,
            PlanHandle = sub.Product?.Handle ?? planHandle,
            Reference = sub.Reference ?? request.Subscription.Reference ?? string.Empty
        };
    }

    public async Task<IReadOnlyList<MySubscriptionInfo>> GetMySubscriptionsAsync(string userReference, CancellationToken ct = default)
    {
        int customerId = await EnsureCustomerAsync(userReference, ct);
        var subs = await _client.Customers.ListCustomerSubscriptions(customerId, ct);
        var result = new List<MySubscriptionInfo>();
        foreach (var item in subs)
        {
            var s = item.Subscription;
            if (s == null) continue;
            result.Add(new MySubscriptionInfo
            {
                Id = s.Id ?? 0,
                State = s.State?.ToString() ?? string.Empty,
                PlanHandle = s.Product?.Handle ?? string.Empty,
                NextBillingDate = s.CurrentPeriodEndsAt?.ToString("yyyy-MM-dd") ?? string.Empty
            });
        }
        return result;
    }

    private async Task<int> EnsureCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var read = await _client.Customers.ReadCustomerByReference(reference, ct);
            if (read?.Customer?.Id != null)
                return read.Customer.Id.Value;
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            // Expected when customer missing; proceed to create
        }

        var createReq = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                Reference = reference,
                FirstName = "Shopper",
                LastName = "User",
                Email = $"{reference}@example.com"
            }
        };

        var created = await _client.Customers.CreateCustomer(createReq, ct);
        if (created?.Customer?.Id != null)
            return created.Customer.Id.Value;

        throw new InvalidOperationException("Failed to create or find Maxio customer for reference: " + reference);
    }

    private static bool IsNotFound(Exception ex)
    {
        // SDK throws SdkException<RawError> for 404s; check if status is 404 via reflection or message
        var typeName = ex.GetType().Name;
        if (typeName.Contains("RawError") || typeName.Contains("SdkException"))
        {
            // Approximate: if message contains 404 or NotFound
            if (ex.Message.Contains("404") || ex.Message.Contains("Not Found")) return true;
        }
        return false;
    }
}
