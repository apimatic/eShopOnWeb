using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of the billing provider abstraction.
///
/// Wire contract (verified against the Advanced Billing full-JSON API):
///   - Base address: https://{subdomain}.chargify.com (US) — the API lives at
///     the site root; requests are authenticated with HTTP Basic auth using
///     the API key as the username and the literal string "x" as the password.
///   - GET  /product_families/lookup.json?handle={handle}
///   - GET  /product_families/{id|handle:x}/products.json
///   - GET  /customers/lookup.json?reference={reference} (404 when absent)
///   - POST /customers.json  { customer: { first_name, last_name, email, reference } }
///   - POST /subscriptions.json { subscription: { product_handle, customer_id,
///          payment_collection_method: "invoice" } }
///   - GET  /subscriptions/{id}.json
/// </summary>
public class MaxioClient : ISubscriptionBillingProvider
{
    public const string CardlessCollectionMethod = "invoice";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CustomerLocks = new();

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, MaxioOptions options, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var products = await GetJsonAsync($"product_families/{familyId}/products.json", cancellationToken);
        var result = new List<BillingPlan>();

        if (products is null)
        {
            return result;
        }

        foreach (var wrapper in products.Value.EnumerateArray())
        {
            var product = wrapper.GetPropertyOrNull("product");
            if (product is null ||
                (product.Value.TryGetProperty("archived_at", out var archived) &&
                 archived.ValueKind == System.Text.Json.JsonValueKind.String))
            {
                continue;
            }

            var handle = product.Value.RequireString("handle");
            var name = product.Value.RequireString("name");
            var price = product.Value.RequireInt64("price_in_cents");
            var interval = product.Value.TryGetProperty("interval", out var iv) && iv.ValueKind == System.Text.Json.JsonValueKind.Number
                ? iv.GetInt32() : 1;
            var unit = product.Value.TryGetProperty("interval_unit", out var iu) && iu.ValueKind == System.Text.Json.JsonValueKind.String
                ? iu.GetString() ?? "month" : "month";

            result.Add(new BillingPlan(handle, name, price, interval, unit));
        }

        return result;
    }

    public async Task<BillingSubscription> GetSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken)
    {
        var document = await GetJsonAsync($"subscriptions/{subscriptionId}.json", cancellationToken);
        var subscription = (document ?? throw new BillingEntityNotFoundException($"Billing subscription {subscriptionId} was not found."))
            .GetPropertyOrNull("subscription")
            ?? throw new MaxioApiException(200, null, $"Subscription {subscriptionId}: unexpected response shape.");
        return MapSubscription(subscription);
    }

    public async Task<BillingSubscription> SubscribeAsync(string customerReference, string email,
        string firstName, string lastName, string planHandle, CancellationToken cancellationToken)
    {
        var customerId = await EnsureCustomerAsync(customerReference, email, firstName, lastName, cancellationToken);

        var payload = new
        {
            subscription = new
            {
                product_handle = planHandle,
                customer_id = customerId,
                payment_collection_method = CardlessCollectionMethod
            }
        };

        var document = await SendJsonAsync(HttpMethod.Post, "subscriptions.json", payload, cancellationToken);
        var subscription = document.GetPropertyOrNull("subscription")
            ?? throw new MaxioApiException(201, null, "Subscription creation returned an unexpected response shape.");
        return MapSubscription(subscription);
    }

    /// <summary>
    /// Returns the billing customer id for the reference, creating the
    /// customer when absent. Serialized per reference so concurrent calls
    /// never create duplicate customers.
    /// </summary>
    private async Task<long> EnsureCustomerAsync(string reference, string email,
        string firstName, string lastName, CancellationToken cancellationToken)
    {
        var names = SplitName(firstName, lastName, email);
        var lookup = await GetJsonAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken, tolerateNotFound: true);

        if (lookup is not null)
        {
            var existing = lookup.Value.GetPropertyOrNull("customer");
            if (existing is not null && existing.Value.TryGetProperty("id", out var id))
            {
                return id.GetInt64();
            }
        }

        var gate = CustomerLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Re-check inside the lock: another caller may have created it.
            lookup = await GetJsonAsync(
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
                cancellationToken, tolerateNotFound: true);
            if (lookup is not null &&
                lookup.Value.GetPropertyOrNull("customer") is { } raced)
            {
                return raced.GetProperty("id").GetInt64();
            }

            var payload = new
            {
                customer = new
                {
                    first_name = names.firstName,
                    last_name = names.lastName,
                    email,
                    reference
                }
            };

            var created = await SendJsonAsync(HttpMethod.Post, "customers.json", payload, cancellationToken);
            var customer = created.GetPropertyOrNull("customer")
                ?? throw new MaxioApiException(201, null, "Customer creation returned an unexpected response shape.");
            return customer.GetProperty("id").GetInt64();
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        var document = await GetJsonAsync(
            $"product_families/lookup.json?handle={Uri.EscapeDataString(_options.ProductFamilyHandle)}",
            cancellationToken, tolerateNotFound: true);

        var family = document is null
            ? null
            : document.Value.GetPropertyOrNull("product_family");
        if (family is null)
        {
            throw new MaxioApiException(404, null,
                $"Product family '{_options.ProductFamilyHandle}' was not found in the billing site.");
        }

        return family.Value.GetProperty("id").GetInt32();
    }

    private static BillingSubscription MapSubscription(System.Text.Json.JsonElement subscription)
    {
        var product = subscription.GetPropertyOrNull("product");
        var handle = product?.TryGetProperty("handle", out var ph) == true && ph.ValueKind == System.Text.Json.JsonValueKind.String
            ? ph.GetString()! : string.Empty;
        var name = product?.TryGetProperty("name", out var pn) == true && pn.ValueKind == System.Text.Json.JsonValueKind.String
            ? pn.GetString()! : string.Empty;
        var price = product != null && product.Value.TryGetProperty("price_in_cents", out var pc) && pc.ValueKind == System.Text.Json.JsonValueKind.Number
            ? pc.GetInt64()
            : subscription.TryGetProperty("product_price_in_cents", out var ppic) && ppic.ValueKind == System.Text.Json.JsonValueKind.Number
                ? ppic.GetInt64() : 0;

        var nextBilling = FirstDate(subscription, "next_billing_at", "current_period_ends_at", "next_assessment_at");

        return new BillingSubscription(
            subscription.GetProperty("id").GetInt64(),
            subscription.TryGetProperty("customer_id", out var customerId) && customerId.ValueKind == System.Text.Json.JsonValueKind.Number
                ? customerId.GetInt64() : 0,
            subscription.RequireString("state"),
            handle,
            name,
            price,
            nextBilling);
    }

    private static DateTime? FirstDate(System.Text.Json.JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var value) &&
                value.ValueKind == System.Text.Json.JsonValueKind.String &&
                DateTime.TryParse(value.GetString(), out var parsed))
            {
                return parsed.ToUniversalTime();
            }
        }

        return null;
    }

    private static (string firstName, string lastName) SplitName(string firstName, string lastName, string email)
    {
        var first = string.IsNullOrWhiteSpace(firstName) ? "eShop" : firstName.Trim();
        var last = string.IsNullOrWhiteSpace(lastName) ? "Customer" : lastName.Trim();
        if (first == last && first == "eShop Customer")
        {
            // Derive something stable and human-readable from the email local part.
            var local = email.Split('@')[0];
            if (!string.IsNullOrWhiteSpace(local))
            {
                last = local;
            }
        }

        return (first, last);
    }

    private async Task<System.Text.Json.JsonElement?> GetJsonAsync(string relativeUrl,
        CancellationToken cancellationToken, bool tolerateNotFound = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        return await SendAsync(request, tolerateNotFound, cancellationToken);
    }

    private async Task<System.Text.Json.JsonElement> SendJsonAsync(HttpMethod method, string relativeUrl,
        object payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        var result = await SendAsync(request, tolerateNotFound: false, cancellationToken);
        return result!.Value;
    }

    private async Task<System.Text.Json.JsonElement?> SendAsync(HttpRequestMessage request,
        bool tolerateNotFound, CancellationToken cancellationToken)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = CreateBasicAuthHeader();

        _logger.LogInformation("Maxio {Method} {Url}", request.Method, request.RequestUri);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if (tolerateNotFound && (int)response.StatusCode == 404)
            {
                return null;
            }

            _logger.LogWarning("Maxio call to {Url} failed with {StatusCode}: {Body}",
                request.RequestUri, (int)response.StatusCode, body);
            throw new MaxioApiException((int)response.StatusCode, body,
                $"Maxio request {(int)response.StatusCode} for {request.RequestUri?.PathAndQuery}: {body}");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        return System.Text.Json.JsonDocument.Parse(body).RootElement.Clone();
    }

    private AuthenticationHeaderValue CreateBasicAuthHeader()
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }
}
