using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class UserSubscription
{
    public string UserIdentity { get; set; } = "";
    public int MaxioCustomerId { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SubscriptionService
{
    private readonly MaxioClient _maxio;
    private readonly ConcurrentDictionary<string, UserSubscription> _store = new();

    public SubscriptionService(MaxioClient maxio) => _maxio = maxio;

    public async Task<UserSubscription?> GetAsync(string userIdentity)
    {
        _store.TryGetValue(userIdentity, out var s);
        if (s != null) return s;
        // Try to discover from Maxio by reference = userIdentity
        try
        {
            var customers = await _maxio.GetCustomersAsync(reference: userIdentity);
            if (customers.TryGetProperty("customers", out var arr) && arr.GetArrayLength() > 0)
            {
                var cust = arr[0].GetProperty("customer");
                int cid = cust.GetProperty("id").GetInt32();
                var subs = await _maxio.GetSubscriptionsAsync(customerId: cid);
                int? sid = null;
                string? handle = null;
                if (subs.TryGetProperty("subscriptions", out var sarr) && sarr.GetArrayLength() > 0)
                {
                    var sub = sarr[0].GetProperty("subscription");
                    sid = sub.GetProperty("id").GetInt32();
                    handle = sub.TryGetProperty("product", out var p) ? p.GetProperty("handle").GetString() : null;
                }
                s = new UserSubscription
                {
                    UserIdentity = userIdentity,
                    MaxioCustomerId = cid,
                    MaxioSubscriptionId = sid,
                    ProductHandle = handle ?? ""
                };
                _store[userIdentity] = s;
            }
        }
        catch { /* best-effort discovery */ }
        return s;
    }

    public async Task<UserSubscription> EnsureCustomerAsync(string userIdentity, string email, string firstName = "", string lastName = "")
    {
        var existing = await GetAsync(userIdentity);
        if (existing != null) return existing;

        // Idempotent: search by reference first
        try
        {
            var customers = await _maxio.GetCustomersAsync(reference: userIdentity);
            if (customers.TryGetProperty("customers", out var arr) && arr.GetArrayLength() > 0)
            {
                var cust = arr[0].GetProperty("customer");
                int cid = cust.GetProperty("id").GetInt32();
                var s = new UserSubscription { UserIdentity = userIdentity, MaxioCustomerId = cid };
                _store[userIdentity] = s;
                return s;
            }
        }
        catch { }

        // Search by email
        try
        {
            var customers = await _maxio.GetCustomersAsync(email: email);
            if (customers.TryGetProperty("customers", out var arr) && arr.GetArrayLength() > 0)
            {
                var cust = arr[0].GetProperty("customer");
                int cid = cust.GetProperty("id").GetInt32();
                var s = new UserSubscription { UserIdentity = userIdentity, MaxioCustomerId = cid };
                _store[userIdentity] = s;
                return s;
            }
        }
        catch { }

        var created = await _maxio.CreateCustomerAsync(
            string.IsNullOrWhiteSpace(firstName) ? "Shopper" : firstName,
            string.IsNullOrWhiteSpace(lastName) ? "User" : lastName,
            email, userIdentity);
        var newCust = created.GetProperty("customer");
        int newId = newCust.GetProperty("id").GetInt32();
        var u = new UserSubscription { UserIdentity = userIdentity, MaxioCustomerId = newId };
        _store[userIdentity] = u;
        return u;
    }

    public async Task<UserSubscription> SubscribeAsync(string userIdentity, string email, string productHandle, string firstName = "", string lastName = "")
    {
        var userSub = await EnsureCustomerAsync(userIdentity, email, firstName, lastName);
        // If already subscribed to same product, return existing
        if (userSub.MaxioSubscriptionId.HasValue && userSub.ProductHandle == productHandle)
            return userSub;

        // Check existing subs at Maxio to avoid duplicates
        try
        {
            var subs = await _maxio.GetSubscriptionsAsync(customerId: userSub.MaxioCustomerId);
            if (subs.TryGetProperty("subscriptions", out var sarr) && sarr.GetArrayLength() > 0)
            {
                for (int i = 0; i < sarr.GetArrayLength(); i++)
                {
                    var sub = sarr[i].GetProperty("subscription");
                    var prod = sub.GetProperty("product");
                    var h = prod.GetProperty("handle").GetString();
                    var sid = sub.GetProperty("id").GetInt32();
                    if (h == productHandle)
                    {
                        userSub.MaxioSubscriptionId = sid;
                        userSub.ProductHandle = h;
                        _store[userIdentity] = userSub;
                        return userSub;
                    }
                }
            }
        }
        catch { }

        var createdSub = await _maxio.CreateSubscriptionAsync(userSub.MaxioCustomerId, productHandle);
        var subObj = createdSub.GetProperty("subscription");
        userSub.MaxioSubscriptionId = subObj.GetProperty("id").GetInt32();
        userSub.ProductHandle = productHandle;
        _store[userIdentity] = userSub;
        return userSub;
    }
}

