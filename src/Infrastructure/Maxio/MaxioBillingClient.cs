using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, explicit client over the Maxio Advanced Billing REST API (formerly Chargify).
/// Authentication is HTTP Basic over TLS: the site API key is the username and "X" the password.
/// Endpoints, request bodies and response shapes are confirmed against developers.maxio.com
/// and the live sandbox (see integration notes).
/// </summary>
public class MaxioBillingClient : IMaxioBillingClient
{
    // GET /customers/lookup.json returns 404 when the reference is unknown; that is an
    // expected "not found", not an error.
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ILogger<MaxioBillingClient> _logger;
    private readonly string _baseUrl;

    public MaxioBillingClient(HttpClient http, IOptions<MaxioOptions> options, ILogger<MaxioBillingClient> logger)
    {
        var maxio = options.Value;

        if (string.IsNullOrWhiteSpace(maxio.ApiKey))
        {
            throw new BillingConfigurationException("Maxio:ApiKey is not configured.");
        }

        _http = http;
        _logger = logger;
        _baseUrl = maxio.ResolveBaseUrl();

        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{maxio.ApiKey}:X"));

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (string.IsNullOrWhiteSpace(_http.DefaultRequestHeaders.UserAgent.ToString()))
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-MaxioIntegration/1.0");
        }
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productFamilyHandle))
        {
            throw new BillingConfigurationException("A product family handle is required to list subscription plans.");
        }

        // The product_family_id segment also accepts "handle:<handle>". Handles are stable across re-seeds.
        var url = $"{_baseUrl}/product_families/handle:{productFamilyHandle}/products.json?per_page=100";
        using var response = await _http.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var envelope = Deserialize<List<MaxioProductEnvelope>>(json);

        return (envelope ?? new List<MaxioProductEnvelope>())
            .Select(e => e.Product)
            .Where(p => p is not null && string.IsNullOrEmpty(p!.ArchivedAt))
            .Select(MapPlan)!
            .ToList();
    }

    public async Task<SubscriptionPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle)) return null;

        var url = $"{_baseUrl}/products/handle/{planHandle}.json";
        using var response = await _http.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var envelope = Deserialize<MaxioProductEnvelope>(json);
        return envelope?.Product is null ? null : MapPlan(envelope.Product);
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"{_baseUrl}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _http.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var envelope = Deserialize<MaxioCustomerEnvelope>(json);
        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(BillingCustomerDraft draft, CancellationToken cancellationToken = default)
    {
        var body = new CreateCustomerBody
        {
            UniquenessToken = Guid.NewGuid().ToString("N"),
            Customer = new CreateCustomerBody.CustomerAttributes
            {
                Reference = draft.Reference,
                Email = draft.Email,
                FirstName = draft.FirstName,
                LastName = draft.LastName
            }
        };

        using var response = await PostJsonAsync("/customers.json", body, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var envelope = Deserialize<MaxioCustomerEnvelope>(json)
            ?? throw new MaxioApiException((int)response.StatusCode, new[] { "Empty response creating customer." }, json);

        return MapCustomer(envelope.Customer ?? throw new MaxioApiException((int)response.StatusCode, new[] { "Missing customer in response." }, json));
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(long customerId, string planHandle, string uniquenessToken, CancellationToken cancellationToken = default)
    {
        var body = new CreateSubscriptionBody
        {
            UniquenessToken = uniquenessToken,
            Subscription = new CreateSubscriptionBody.SubscriptionAttributes
            {
                ProductHandle = planHandle,
                CustomerId = customerId
            }
        };

        using var response = await PostJsonAsync("/subscriptions.json", body, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var envelope = Deserialize<MaxioSubscriptionEnvelope>(json)
            ?? throw new MaxioApiException((int)response.StatusCode, new[] { "Empty response creating subscription." }, json);

        return MapSubscription(envelope.Subscription ?? throw new MaxioApiException((int)response.StatusCode, new[] { "Missing subscription in response." }, json));
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsForCustomerAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var url = $"{_baseUrl}/customers/{customerId}/subscriptions.json?per_page=100";
        using var response = await _http.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var envelope = Deserialize<List<MaxioSubscriptionEnvelope>>(json);

        return (envelope ?? new List<MaxioSubscriptionEnvelope>())
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(MapSubscription!)
            .OrderByDescending(s => s.ActivatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private async Task<HttpResponseMessage> PostJsonAsync(string path, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var url = _baseUrl + path;
        _logger.LogDebug("Maxio POST {Url}", url);
        return await _http.PostAsync(url, content, cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ParseErrors(body);
        _logger.LogWarning("Maxio request to {Url} failed: HTTP {Status} {Errors}",
            response.RequestMessage?.RequestUri?.ToString(), (int)response.StatusCode, string.Join("; ", errors));

        throw new MaxioApiException((int)response.StatusCode, errors, body);
    }

    private static List<string> ParseErrors(string body)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(body)) return errors;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errorsEl))
            {
                switch (errorsEl.ValueKind)
                {
                    case JsonValueKind.Array:
                        foreach (var item in errorsEl.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String) errors.Add(item.GetString()!);
                            else errors.Add(item.GetRawText());
                        }
                        break;
                    case JsonValueKind.String:
                        errors.Add(errorsEl.GetString()!);
                        break;
                    default:
                        errors.Add(errorsEl.GetRawText());
                        break;
                }
            }
        }
        catch (JsonException)
        {
            errors.Add(body);
        }

        if (errors.Count == 0) errors.Add(body);
        return errors;
    }

    private static T? Deserialize<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    private static SubscriptionPlan MapPlan(MaxioProductWire p) => new()
    {
        Handle = p.Handle ?? string.Empty,
        Name = p.Name ?? string.Empty,
        Description = p.Description,
        PriceInCents = p.PriceInCents,
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit ?? string.Empty,
        Taxable = p.Taxable,
        RequiresPaymentMethod = p.RequireCreditCard,
        ProductFamilyHandle = p.ProductFamily?.Handle ?? string.Empty
    };

    private static BillingCustomer MapCustomer(MaxioCustomerWire c) => new()
    {
        Id = c.Id,
        Reference = c.Reference ?? string.Empty,
        Email = c.Email,
        FirstName = c.FirstName,
        LastName = c.LastName
    };

    private static BillingSubscription MapSubscription(MaxioSubscriptionWire s) => new()
    {
        Id = s.Id,
        CustomerId = s.Customer?.Id ?? 0,
        State = s.State ?? string.Empty,
        PlanHandle = s.Product?.Handle ?? string.Empty,
        PlanName = s.Product?.Name ?? string.Empty,
        PriceInCents = s.ProductPriceInCents != 0 ? s.ProductPriceInCents : (s.Product?.PriceInCents ?? 0),
        Currency = s.Currency ?? string.Empty,
        ActivatedAt = ParseDate(s.ActivatedAt),
        CurrentPeriodEndsAt = ParseDate(s.CurrentPeriodEndsAt),
        NextBillingAt = ParseDate(s.NextAssessmentAt) ?? ParseDate(s.CurrentPeriodEndsAt)
    };

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }
}
