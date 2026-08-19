using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// HTTP client for Maxio Advanced Billing (formerly Chargify). Endpoints and
/// payload shapes are taken from the official Advanced Billing API:
/// https://developers.maxio.com/ and the Maxio .NET SDK docs (9.1.0).
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient http, IOptions<MaxioSettings> options, ILogger<MaxioClient> logger)
    {
        _http = http;
        _settings = options.Value;
        _logger = logger;
        ConfigureHttpClient();
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListProductsForConfiguredFamilyAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
        }

        var familyId = Uri.EscapeDataString($"handle:{_settings.ProductFamilyHandle}");
        var envelopes = await GetAsync<List<ProductEnvelope>>(
            $"product_families/{familyId}/products.json?include_archived=false&per_page=200",
            cancellationToken);

        return (envelopes ?? new List<ProductEnvelope>())
            .Select(e => e.Product)
            .Where(p => p != null && p.ArchivedAt == null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => p!.ToPlan())
            .ToList();
    }

    public async Task<BillingCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var envelope = await GetOrNotFoundAsync<CustomerEnvelope>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);
        return envelope?.Customer?.ToBillingCustomer();
    }

    public async Task<BillingCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var body = new CustomerEnvelope
        {
            Customer = new MaxioCustomerPayload
            {
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            }
        };

        var created = await SendAsync<CustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken);
        if (created?.Customer == null)
        {
            throw new MaxioApiException(502, "Maxio created a customer but returned an empty payload.");
        }

        return created.Customer.ToBillingCustomer();
    }

    public async Task<ShopperSubscription?> GetSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var envelope = await GetOrNotFoundAsync<SubscriptionEnvelope>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);
        return envelope?.Subscription?.ToShopperSubscription();
    }

    public async Task<ShopperSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var body = new SubscriptionEnvelope
        {
            Subscription = new MaxioSubscriptionPayload
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                Reference = reference,
                // Remittance (invoice) collection does not require a card on file.
                // Seeded plans are configured with payment method not required.
                PaymentCollectionMethod = "remittance"
            }
        };

        var created = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        if (created?.Subscription == null)
        {
            throw new MaxioApiException(502, "Maxio created a subscription but returned an empty payload.");
        }

        return created.Subscription.ToShopperSubscription();
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var envelopes = await GetAsync<List<SubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken);

        return (envelopes ?? new List<SubscriptionEnvelope>())
            .Select(e => e.Subscription?.ToShopperSubscription())
            .Where(s => s != null)
            .Select(s => s!)
            .ToList();
    }

    private void ConfigureHttpClient()
    {
        if (!_settings.IsConfigured)
        {
            return;
        }

        _http.BaseAddress ??= new Uri(_settings.GetApiBaseUrl().TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (_http.DefaultRequestHeaders.Authorization is null)
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new InvalidOperationException(
                "Maxio is not configured. Set Maxio:ApiKey and Maxio:Subdomain (or Maxio:BaseUrl) via environment or user-secrets.");
        }

        if (_http.BaseAddress is null)
        {
            ConfigureHttpClient();
        }
    }

    private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(relativeUrl, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, payload, relativeUrl);
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private async Task<T?> GetOrNotFoundAsync<T>(string relativeUrl, CancellationToken cancellationToken) where T : class
    {
        using var response = await _http.GetAsync(relativeUrl, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, payload, relativeUrl);
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string relativeUrl, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(method, relativeUrl) { Content = content };
        using var response = await _http.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, payload, relativeUrl);
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private MaxioApiException CreateApiException(HttpStatusCode statusCode, string payload, string relativeUrl)
    {
        var message = ParseErrorMessage(payload) ?? $"Maxio request to {relativeUrl} failed with {(int)statusCode}.";
        _logger.LogWarning("Maxio API {Url} returned {StatusCode}: {Message}", relativeUrl, (int)statusCode, message);
        return new MaxioApiException((int)statusCode, message);
    }

    private static string? ParseErrorMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    var parts = errors.EnumerateArray()
                        .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                        .Where(s => !string.IsNullOrWhiteSpace(s));
                    return string.Join(" ", parts);
                }

                if (errors.ValueKind == JsonValueKind.Object)
                {
                    var parts = errors.EnumerateObject()
                        .Select(p => $"{p.Name}: {p.Value}");
                    return string.Join(" ", parts);
                }

                return errors.ToString();
            }
        }
        catch (JsonException)
        {
            // Fall through to raw payload.
        }

        return payload.Length > 500 ? payload[..500] : payload;
    }

    private sealed class ProductEnvelope
    {
        public MaxioProductPayload? Product { get; set; }
    }

    private sealed class CustomerEnvelope
    {
        public MaxioCustomerPayload? Customer { get; set; }
    }

    private sealed class SubscriptionEnvelope
    {
        public MaxioSubscriptionPayload? Subscription { get; set; }
    }

    private sealed class MaxioProductPayload
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Id { get; set; }
        public string? Handle { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public long? PriceInCents { get; set; }
        public int? Interval { get; set; }
        public string? IntervalUnit { get; set; }
        public DateTimeOffset? ArchivedAt { get; set; }

        public SubscriptionPlan ToPlan() => new()
        {
            Id = Id,
            Handle = Handle ?? string.Empty,
            Name = Name ?? string.Empty,
            Description = Description,
            Price = ToDollars(PriceInCents),
            Interval = Interval ?? 0,
            IntervalUnit = IntervalUnit ?? string.Empty
        };
    }

    private sealed class MaxioCustomerPayload
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Id { get; set; }
        public string? Reference { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }

        public BillingCustomer ToBillingCustomer() => new()
        {
            Id = Id,
            Reference = Reference,
            Email = Email ?? string.Empty
        };
    }

    private sealed class MaxioSubscriptionPayload
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Id { get; set; }
        public string? State { get; set; }
        public long? ProductPriceInCents { get; set; }
        public DateTimeOffset? NextAssessmentAt { get; set; }
        public int? CustomerId { get; set; }
        public string? ProductHandle { get; set; }
        public string? Reference { get; set; }
        public string? PaymentCollectionMethod { get; set; }
        public MaxioProductPayload? Product { get; set; }

        public ShopperSubscription ToShopperSubscription() => new()
        {
            Id = Id,
            ProductHandle = Product?.Handle ?? ProductHandle ?? string.Empty,
            ProductName = Product?.Name ?? string.Empty,
            Price = ToDollars(ProductPriceInCents ?? Product?.PriceInCents),
            State = State ?? string.Empty,
            NextBillingAt = NextAssessmentAt
        };
    }

    private static decimal ToDollars(long? cents) =>
        cents.HasValue ? cents.Value / 100m : 0m;
}
