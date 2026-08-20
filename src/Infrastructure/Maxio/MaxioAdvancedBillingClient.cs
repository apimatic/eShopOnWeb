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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for Maxio Advanced Billing (Billing API).
/// Authentication is HTTP Basic with the API key as username and "X" as password,
/// against <c>https://{subdomain}.chargify.com</c> unless Maxio:BaseUrl is set.
/// </summary>
public class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient http,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        var handleSegment = $"handle:{Uri.EscapeDataString(productFamilyHandle)}";
        var products = new List<MaxioProduct>();

        for (var page = 1; ; page++)
        {
            var path = $"product_families/{handleSegment}/products.json?page={page}&per_page=200";
            var payload = await SendAsync<List<ProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken);
            if (payload is null || payload.Count == 0)
            {
                break;
            }

            products.AddRange(payload.Where(e => e.Product is not null).Select(e => MapProduct(e.Product!)));
            if (payload.Count < 200)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken);
            return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
        }
        catch (MaxioBillingException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        CreateMaxioCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var body = new CustomerEnvelope
        {
            Customer = new CustomerDto
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Reference = request.Reference
            }
        };

        var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioBillingException(502, "Maxio returned an empty customer create response.");
        }

        return MapCustomer(envelope.Customer);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var payload = await SendAsync<List<SubscriptionEnvelope>>(HttpMethod.Get, path, null, cancellationToken);
        if (payload is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return payload.Where(e => e.Subscription is not null).Select(e => MapSubscription(e.Subscription!)).ToList();
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Get, path, null, cancellationToken);
            return envelope?.Subscription is null ? null : MapSubscription(envelope.Subscription);
        }
        catch (MaxioBillingException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateMaxioSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionBody
        {
            Subscription = new CreateSubscriptionDto
            {
                CustomerId = request.CustomerId,
                ProductHandle = request.ProductHandle,
                Reference = request.Reference,
                PaymentCollectionMethod = request.PaymentCollectionMethod
            },
            UniquenessToken = request.UniquenessToken
        };

        var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioBillingException(502, "Maxio returned an empty subscription create response.");
        }

        return MapSubscription(envelope.Subscription);
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string relativePath, object? body, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        HttpResponseMessage? response = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativePath);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode != (HttpStatusCode)429 || attempt == maxAttempts)
            {
                break;
            }

            _logger.LogWarning("Maxio returned 429 for {Method} {Path}; retrying ({Attempt}/{Max}).",
                method, SanitizePath(relativePath), attempt, maxAttempts);
            response.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
        }

        using (response)
        {
            var content = response is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);
            if (response is null)
            {
                throw new MaxioBillingException(502, "No response from Maxio Advanced Billing.");
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                throw new DuplicateException("A duplicate Maxio request was detected.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var message = TryFormatError(content) ?? $"Maxio request failed with status {(int)response.StatusCode}.";
                _logger.LogWarning("Maxio {Method} {Path} failed with {Status}: {Message}",
                    method, SanitizePath(relativePath), (int)response.StatusCode, message);
                throw new MaxioBillingException((int)response.StatusCode, message);
            }

            if (string.IsNullOrWhiteSpace(content) || content == "[]")
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(content, JsonOptions);
        }
    }

    private static string SanitizePath(string path)
    {
        var q = path.IndexOf('?', StringComparison.Ordinal);
        return q >= 0 ? path[..q] : path;
    }

    private static string? TryFormatError(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    var parts = errors.EnumerateArray()
                        .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                        .Where(s => !string.IsNullOrWhiteSpace(s));
                    return string.Join(" ", parts!);
                }

                if (errors.ValueKind == JsonValueKind.String)
                {
                    return errors.GetString();
                }

                return errors.ToString();
            }
        }
        catch (JsonException)
        {
            // Fall through to truncated raw content.
        }

        return content.Length <= 500 ? content : content[..500];
    }

    private static MaxioProduct MapProduct(ProductDto dto) => new()
    {
        Id = dto.Id ?? 0,
        Handle = dto.Handle ?? string.Empty,
        Name = dto.Name ?? string.Empty,
        Description = dto.Description,
        PriceInCents = dto.PriceInCents ?? 0,
        Interval = dto.Interval ?? 0,
        IntervalUnit = dto.IntervalUnit ?? string.Empty,
        ProductFamilyHandle = dto.ProductFamily?.Handle ?? string.Empty,
        Archived = dto.ArchivedAt is not null
    };

    private static MaxioCustomer MapCustomer(CustomerDto dto) => new()
    {
        Id = dto.Id ?? 0,
        Reference = dto.Reference,
        Email = dto.Email ?? string.Empty
    };

    private static MaxioSubscription MapSubscription(SubscriptionDto dto) => new()
    {
        Id = dto.Id ?? 0,
        State = dto.State ?? string.Empty,
        Reference = dto.Reference,
        ProductPriceInCents = dto.ProductPriceInCents ?? 0,
        CurrentPeriodEndsAt = dto.CurrentPeriodEndsAt,
        ProductHandle = dto.Product?.Handle ?? string.Empty,
        ProductName = dto.Product?.Name ?? string.Empty
    };

    private sealed class ProductEnvelope
    {
        public ProductDto? Product { get; set; }
    }

    private sealed class ProductDto
    {
        public long? Id { get; set; }
        public string? Handle { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public long? PriceInCents { get; set; }
        public int? Interval { get; set; }
        public string? IntervalUnit { get; set; }
        public DateTimeOffset? ArchivedAt { get; set; }
        public ProductFamilyDto? ProductFamily { get; set; }
    }

    private sealed class ProductFamilyDto
    {
        public string? Handle { get; set; }
    }

    private sealed class CustomerEnvelope
    {
        public CustomerDto? Customer { get; set; }
    }

    private sealed class CustomerDto
    {
        public long? Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Reference { get; set; }
    }

    private sealed class SubscriptionEnvelope
    {
        public SubscriptionDto? Subscription { get; set; }
    }

    private sealed class SubscriptionDto
    {
        public long? Id { get; set; }
        public string? State { get; set; }
        public string? Reference { get; set; }
        public long? ProductPriceInCents { get; set; }
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        public ProductDto? Product { get; set; }
    }

    private sealed class CreateSubscriptionBody
    {
        public CreateSubscriptionDto? Subscription { get; set; }
        public string? UniquenessToken { get; set; }
    }

    private sealed class CreateSubscriptionDto
    {
        public long CustomerId { get; set; }
        public string? ProductHandle { get; set; }
        public string? Reference { get; set; }
        public string? PaymentCollectionMethod { get; set; }
    }
}
