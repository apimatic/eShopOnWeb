using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi;

public static class SubscriptionEndpoints
{
    public static async Task<IResult> ListPlans(IMaxioBillingClient client, MaxioSettings settings)
    {
        var familyHandle = settings.ProductFamilyHandle;
        var resp = await client.ListProductsForFamilyHandleAsync(familyHandle);
        if (resp == null) return Results.Ok(new { plans = Array.Empty<object>() });
        var items = resp["items"]?.AsArray() ?? new JsonArray();
        var plans = new List<object>();
        foreach (var item in items)
        {
            var p = item?["product"];
            if (p == null) continue;
            plans.Add(new
            {
                handle = p["handle"]?.GetValue<string>(),
                name = p["name"]?.GetValue<string>(),
                priceInCents = p["price_in_cents"]?.GetValue<long>(),
                intervalUnit = p["interval_unit"]?.GetValue<string>(),
                interval = p["interval"]?.GetValue<int>()
            });
        }
        return Results.Ok(new { plans });
    }

    public static async Task<IResult> CreateSubscription(IMaxioBillingClient client, MaxioSettings settings, ClaimsPrincipal user, HttpRequest request)
    {
        // Read body
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(body);
        var productHandle = doc.RootElement.GetProperty("productHandle").GetString() ?? "";
        // Idempotent customer: use user id claim or email
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.Identity?.Name ?? "unknown";
        var email = user.FindFirst(ClaimTypes.Email)?.Value ?? userId + "@localhost";
        // Try find by reference = userId
        var existingCustomer = await client.GetCustomerByReferenceAsync(userId);
        int customerId;
        if (existingCustomer != null && existingCustomer["customer"] != null)
        {
            customerId = existingCustomer["customer"]!["id"]!.GetValue<int>();
        }
        else
        {
            var name = user.Identity?.Name ?? userId;
            var parts = name.Split(' ', 2);
            var result = await client.CreateCustomerAsync(userId, parts[0], parts.Length > 1 ? parts[1] : "", email);
            customerId = result!["customer"]!["id"]!.GetValue<int>();
        }
        var sub = await client.CreateSubscriptionAsync(productHandle, customerId);
        return Results.Ok(new
        {
            subscription = new
            {
                id = sub?["subscription"]?["id"]?.GetValue<int>(),
                state = sub?["subscription"]?["state"]?.GetValue<string>(),
                productHandle = sub?["subscription"]?["product"]?["handle"]?.GetValue<string>(),
                nextBillingAt = sub?["subscription"]?["next_billing_at"]?.GetValue<string>(),
                customerId = sub?["subscription"]?["customer_id"]?.GetValue<int>()
            }
        });
    }

    public static async Task<IResult> ListMySubscriptions(IMaxioBillingClient client, MaxioSettings settings, ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.Identity?.Name ?? "unknown";
        var existingCustomer = await client.GetCustomerByReferenceAsync(userId);
        if (existingCustomer == null || existingCustomer["customer"] == null)
            return Results.Ok(new { subscriptions = Array.Empty<object>() });
        var customerId = existingCustomer["customer"]!["id"]!.GetValue<int>();
        var resp = await client.ListSubscriptionsForCustomerAsync(customerId);
        var subs = new List<object>();
        if (resp != null && resp["subscriptions"] != null)
        {
            foreach (var s in resp["subscriptions"]!.AsArray()!)
            {
                subs.Add(new
                {
                    id = s?["id"]?.GetValue<int>(),
                    state = s?["state"]?.GetValue<string>(),
                    productHandle = s?["product"]?["handle"]?.GetValue<string>(),
                    nextBillingAt = s?["next_billing_at"]?.GetValue<string>(),
                    customerId = s?["customer_id"]?.GetValue<int>()
                });
            }
        }
        return Results.Ok(new { subscriptions = subs });
    }
}
