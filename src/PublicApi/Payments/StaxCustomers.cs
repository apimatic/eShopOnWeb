using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Stax customer and payment-method lookups, keyed by the shopper's email.</summary>
public class StaxCustomers
{
    private readonly StaxClient _stax;

    public StaxCustomers(StaxClient stax) => _stax = stax;

    /// <summary>All non-deleted Stax customers whose email is exactly <paramref name="email"/>.</summary>
    public async Task<List<JsonNode>> FindAsync(string email, CancellationToken ct = default)
    {
        var result = new List<JsonNode>();
        for (var page = 1; page <= 50; page++)
        {
            var json = await _stax.GetAsync($"customer?email={StaxClient.Q(email)}&per_page=100&page={page}", ct);
            var data = json["data"] as JsonArray;
            if (data == null) break;
            foreach (var c in data)
            {
                if (c == null || c["deleted_at"] != null) continue;
                if (string.Equals((string?)c["email"], email, StringComparison.OrdinalIgnoreCase)) result.Add(c);
            }
            var last = (int?)json["last_page"] ?? page;
            if (page >= last) break;
        }
        return result;
    }

    public async Task<string?> FindIdAsync(string email, CancellationToken ct = default)
        => (string?)(await FindAsync(email, ct)).FirstOrDefault()?["id"];

    public async Task<string> FindOrCreateAsync(string email, string? company, CancellationToken ct = default)
    {
        var existing = (await FindAsync(email, ct)).FirstOrDefault();
        if (existing != null)
        {
            if (!string.IsNullOrWhiteSpace(company) && (string?)existing["company"] != company)
            {
                await _stax.PutAsync("customer/" + (string)existing["id"]!, new { company }, ct);
            }
            return (string)existing["id"]!;
        }

        var created = await _stax.PostAsync("customer", new
        {
            firstname = email.Split('@')[0],
            lastname = "Shopper",
            email,
            company = company ?? "",
            reference = "eshoponweb"
        }, ct);
        return (string)created["id"]!;
    }

    /// <summary>Payment methods of a customer: the active ones, or (deleted = true) the removed ones.</summary>
    public async Task<List<JsonNode>> ListMethodsAsync(string customerId, bool deleted, CancellationToken ct = default)
    {
        var result = new List<JsonNode>();
        if (!deleted)
        {
            // The merchant-wide list leaves out active bank accounts; the customer's own list has everything.
            var json = await _stax.GetAsync($"customer/{StaxClient.Q(customerId)}/payment-method", ct);
            var items = json as JsonArray ?? json["data"] as JsonArray;
            if (items != null) result.AddRange(items.Where(m => m != null && m["deleted_at"] == null)!);
            return result;
        }

        for (var page = 1; page <= 50; page++)
        {
            var json = await _stax.GetAsync(
                $"payment-method?customer_id={StaxClient.Q(customerId)}&per_page=200&page={page}&status=deleted", ct);
            var data = json["data"] as JsonArray;
            if (data == null) break;
            foreach (var m in data)
            {
                if (m == null || (string?)m["customer_id"] != customerId || m["deleted_at"] == null) continue;
                result.Add(m);
            }
            var last = (int?)json["last_page"] ?? page;
            if (page >= last) break;
        }
        return result;
    }

    /// <summary>The payment method if it exists, belongs to this shopper and has not been removed.</summary>
    public async Task<JsonNode> GetOwnedActiveMethodAsync(string email, string paymentMethodId, CancellationToken ct = default)
    {
        var customerIds = (await FindAsync(email, ct)).Select(c => (string?)c["id"]).ToList();
        JsonNode method;
        try
        {
            method = await _stax.GetAsync("payment-method/" + StaxClient.Q(paymentMethodId), ct);
        }
        catch (ApiException ex) when (ex.StatusCode is 404 or 422)
        {
            throw new ApiException(404, "Payment method not found.");
        }
        if (!customerIds.Contains((string?)method["customer_id"]) || method["deleted_at"] != null)
        {
            throw new ApiException(404, "Payment method not found.");
        }
        return method;
    }

    public static object ToView(JsonNode m, bool includeRemoved = false)
    {
        var isCard = (string?)m["method"] == "card";
        return new
        {
            id = (string?)m["id"],
            type = (string?)m["method"],
            nickname = (string?)m["nickname"],
            personName = (string?)m["person_name"],
            brand = isCard ? (string?)m["card_type"] : null,
            last4 = (string?)m["card_last_four"],
            cardExp = isCard ? (string?)m["card_exp"] : null,
            bankName = (string?)m["bank_name"],
            bankType = (string?)m["bank_type"],
            bankHolderType = (string?)m["bank_holder_type"],
            createdAt = (string?)m["created_at"],
            removedAt = includeRemoved ? (string?)m["deleted_at"] : null
        };
    }
}
