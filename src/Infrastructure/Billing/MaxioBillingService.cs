using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing HTTP client. Maxio is the system of record for customers and subscriptions.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> LiveOrProblemStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "assessing",
        "pending",
        "trialing",
        "paused",
        "past_due",
        "soft_failure",
        "unpaid",
        "awaiting_signup"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly HttpClient _http;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        HttpClient http,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        ConfigureHttpClient(_http, _options);
    }

    internal static void ConfigureHttpClient(HttpClient http, MaxioOptions options)
    {
        http.BaseAddress = new Uri(ResolveBaseUrl(options));
        http.Timeout = TimeSpan.FromSeconds(100);
        http.DefaultRequestHeaders.Accept.Clear();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }

    public static string ResolveBaseUrl(MaxioOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.TrimEnd('/') + "/";
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            return "https://localhost/";
        }

        return $"https://{options.Subdomain}.chargify.com/";
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured(requireFamily: true);

        var familyKey = $"handle:{_options.ProductFamilyHandle}";
        var path = $"product_families/{familyKey}/products.json?per_page=200";
        var envelopes = await GetJsonAsync<List<MaxioProductEnvelope>>(path, cancellationToken);

        return (envelopes ?? new List<MaxioProductEnvelope>())
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => ToPlan(p!))
            .ToList();
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured(requireFamily: false);
        Guard.AgainstNullShopper(shopper);

        var customer = await FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var envelopes = await GetJsonAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customer.Id}/subscriptions.json",
            cancellationToken);

        return (envelopes ?? new List<MaxioSubscriptionEnvelope>())
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => ToSubscription(s!, alreadyExisted: false))
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured(requireFamily: true);
        Guard.AgainstNullShopper(shopper);

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required to subscribe.", nameof(productHandle));
        }

        productHandle = productHandle.Trim();

        var gateKey = $"{shopper.UserId}:{productHandle}";
        var gate = SubscribeLocks.GetOrAdd(gateKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var plans = await ListPlansAsync(cancellationToken);
            if (!plans.Any(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
            {
                throw new SubscriptionPlanNotFoundException(productHandle);
            }

            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var existing = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
            if (existing is not null)
            {
                return existing with { AlreadyExisted = true };
            }

            var uniquenessToken = Guid.NewGuid().ToString("D");
            var payload = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = "remittance"
            };

            var response = await SendAsync(
                HttpMethod.Post,
                "subscriptions.json",
                Wrap("subscription", payload, uniquenessToken),
                cancellationToken);

            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
            {
                _logger.LogInformation(
                    "Maxio returned {Status} creating subscription for shopper {UserId} plan {Plan}; checking for an existing live subscription.",
                    (int)response.StatusCode,
                    shopper.UserId,
                    productHandle);
                var afterConflict = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
                if (afterConflict is not null)
                {
                    return afterConflict with { AlreadyExisted = true };
                }
            }

            await EnsureSuccessAsync(response, "create subscription");
            var created = await ReadJsonAsync<MaxioSubscriptionEnvelope>(response);
            if (created?.Subscription is null)
            {
                throw new MaxioApiException(502, "Maxio created a subscription but returned an empty body.");
            }

            return ToSubscription(created.Subscription, alreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ShopperName.From(shopper);
        var payload = new MaxioCreateCustomer
        {
            FirstName = firstName,
            LastName = lastName,
            Email = shopper.Email,
            Reference = shopper.UserId
        };

        var uniquenessToken = Guid.NewGuid().ToString("D");
        var response = await SendAsync(
            HttpMethod.Post,
            "customers.json",
            Wrap("customer", payload, uniquenessToken),
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            // Unique reference collision from a concurrent signup — look the customer up again.
            var raced = await FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }
        }

        await EnsureSuccessAsync(response, "create customer");
        var created = await ReadJsonAsync<MaxioCustomerEnvelope>(response);
        if (created?.Customer is null)
        {
            throw new MaxioApiException(502, "Maxio created a customer but returned an empty body.");
        }

        return created.Customer;
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync(HttpMethod.Get, path, body: null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "lookup customer");
        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response);
        return envelope?.Customer;
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var envelopes = await GetJsonAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken);

        var match = (envelopes ?? new List<MaxioSubscriptionEnvelope>())
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .FirstOrDefault(s =>
                IsLiveOrProblem(s!.State)
                && string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));

        return match is null ? null : ToSubscription(match, alreadyExisted: true);
    }

    private async Task<T?> GetJsonAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, relativeUrl, body: null, cancellationToken);
        await EnsureSuccessAsync(response, $"GET {relativeUrl}");
        return await ReadJsonAsync<T>(response);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativeUrl,
        JsonNode? body,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativeUrl);
            if (body is not null)
            {
                request.Content = new StringContent(body.ToJsonString(MaxioJson.Options), Encoding.UTF8, "application/json");
            }

            last = await _http.SendAsync(request, cancellationToken);
            if (last.StatusCode != HttpStatusCode.TooManyRequests)
            {
                return last;
            }

            _logger.LogWarning("Maxio returned 429 for {Method} {Url} (attempt {Attempt}). Backing off.", method, relativeUrl, attempt + 1);
            await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), cancellationToken);
        }

        return last!;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var detail = MaxioErrorFormatter.Format(body);
        var status = (int)response.StatusCode;
        throw new MaxioApiException(status, $"Maxio {operation} failed ({status}): {detail}");
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, MaxioJson.Options);
    }

    private static JsonObject Wrap(string resourceName, object resource, string uniquenessToken)
    {
        return new JsonObject
        {
            [resourceName] = JsonSerializer.SerializeToNode(resource, MaxioJson.Options),
            ["uniqueness_token"] = uniquenessToken
        };
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) =>
        new(
            product.Handle!,
            product.Name ?? product.Handle!,
            product.Description,
            CentsToDecimal(product.PriceInCents),
            product.Interval,
            product.IntervalUnit ?? "month");

    private static CustomerSubscription ToSubscription(MaxioSubscription subscription, bool alreadyExisted) =>
        new(
            subscription.Id,
            subscription.State ?? "unknown",
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            CentsToDecimal(subscription.ProductPriceInCents != 0
                ? subscription.ProductPriceInCents
                : subscription.Product?.PriceInCents ?? 0),
            subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            alreadyExisted);

    internal static decimal CentsToDecimal(long cents) => cents / 100m;

    internal static bool IsLiveOrProblem(string? state) =>
        !string.IsNullOrWhiteSpace(state) && LiveOrProblemStates.Contains(state);

    private void EnsureConfigured(bool requireFamily)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            (string.IsNullOrWhiteSpace(_options.Subdomain) && string.IsNullOrWhiteSpace(_options.BaseUrl)))
        {
            throw new MaxioConfigurationException(
                "Maxio billing is not configured. Set Maxio:ApiKey and Maxio:Subdomain (or Maxio:BaseUrl).");
        }

        if (requireFamily && string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio billing is not configured. Set Maxio:ProductFamilyHandle.");
        }
    }

    private static class Guard
    {
        public static void AgainstNullShopper(ShopperIdentity shopper)
        {
            if (shopper is null)
            {
                throw new ArgumentNullException(nameof(shopper));
            }

            if (string.IsNullOrWhiteSpace(shopper.UserId))
            {
                throw new ArgumentException("Shopper user id is required.", nameof(shopper));
            }

            if (string.IsNullOrWhiteSpace(shopper.Email))
            {
                throw new ArgumentException("Shopper email is required.", nameof(shopper));
            }
        }
    }
}

internal static class ShopperName
{
    public static (string First, string Last) From(ShopperIdentity shopper)
    {
        var source = !string.IsNullOrWhiteSpace(shopper.UserName) ? shopper.UserName! : shopper.Email;
        var at = source.IndexOf('@');
        var local = at > 0 ? source[..at] : source;
        if (string.IsNullOrWhiteSpace(local))
        {
            local = "Shopper";
        }

        var first = char.ToUpperInvariant(local[0]) + (local.Length > 1 ? local[1..] : string.Empty);
        return (first, "Customer");
    }
}
