using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed <see cref="HttpClient"/> implementation of <see cref="IMaxioClient"/> talking to the
/// Maxio Advanced Billing REST API. The base address and HTTP Basic Authentication header are
/// configured on the injected <see cref="HttpClient"/> at registration time. This client is
/// deliberately thin: it maps one method to one Maxio endpoint and surfaces failures as
/// <see cref="MaxioApiException"/>. It honours Maxio's concurrency-based rate limiting by
/// retrying <c>429 Too Many Requests</c> (and transient gateway errors) with backoff.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private const int MaxAttempts = 4;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListProductFamilyPlansAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productFamilyHandle))
        {
            throw new MaxioApiException("A Maxio product family handle must be configured to list plans.", 400);
        }

        // The product family may be addressed by id or by "handle:" prefix.
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200";
        using var response = await SendAsync(HttpMethod.Get, path, body: null, cancellationToken);
        var envelopes = await ReadAsync<List<ProductEnvelope>>(response, cancellationToken) ?? new List<ProductEnvelope>();

        return envelopes
            .Where(e => e.Product is not null && e.Product.ArchivedAt is null)
            .Select(e => MapPlan(e.Product!))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(HttpMethod.Get, path, body: null, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(NewCustomer customer, CancellationToken cancellationToken = default)
    {
        var body = new CustomerEnvelope
        {
            Customer = new CustomerDto
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        using var response = await SendAsync(HttpMethod.Post, "customers.json", body, cancellationToken);
        var envelope = await ReadAsync<CustomerEnvelope>(response, cancellationToken);

        if (envelope?.Customer is null)
        {
            throw new MaxioApiException("Maxio did not return a customer when creating one.", (int)response.StatusCode);
        }

        return MapCustomer(envelope.Customer);
    }

    public async Task<SubscriptionSummary> CreateSubscriptionAsync(NewSubscription subscription, CancellationToken cancellationToken = default)
    {
        var body = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionDto
            {
                ProductHandle = subscription.ProductHandle,
                CustomerReference = subscription.CustomerReference,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod
            },
            UniquenessToken = subscription.UniquenessToken
        };

        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        var envelope = await ReadAsync<SubscriptionEnvelope>(response, cancellationToken);

        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException("Maxio did not return a subscription when creating one.", (int)response.StatusCode);
        }

        return MapSubscription(envelope.Subscription);
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json?per_page=200";
        using var response = await SendAsync(HttpMethod.Get, path, body: null, cancellationToken);
        var envelopes = await ReadAsync<List<SubscriptionEnvelope>>(response, cancellationToken) ?? new List<SubscriptionEnvelope>();

        return envelopes
            .Where(e => e.Subscription is not null)
            .Select(e => MapSubscription(e.Subscription!))
            .ToList();
    }

    /// <summary>
    /// Sends a request, retrying on <c>429</c> and transient gateway errors with backoff that
    /// respects a <c>Retry-After</c> header when present. Callers own the returned response and
    /// are responsible for interpreting its status code.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, body.GetType(), JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "Maxio request {Method} {Path} failed (attempt {Attempt}); retrying.", method, path, attempt);
                await Task.Delay(BackoffDelay(attempt, retryAfter: null), cancellationToken);
                continue;
            }

            var isRetryable = response.StatusCode == HttpStatusCode.TooManyRequests
                || (int)response.StatusCode is 502 or 503 or 504;

            if (isRetryable && attempt < MaxAttempts)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta;
                response.Dispose();
                _logger.LogWarning("Maxio returned {Status} for {Method} {Path} (attempt {Attempt}); backing off.",
                    (int)response.StatusCode, method, path, attempt);
                await Task.Delay(BackoffDelay(attempt, retryAfter), cancellationToken);
                continue;
            }

            return response;
        }
    }

    private static TimeSpan BackoffDelay(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        // 2s, 4s, 8s ...
        return TimeSpan.FromSeconds(Math.Pow(2, attempt));
    }

    /// <summary>Reads a successful response body, or throws a <see cref="MaxioApiException"/> describing the failure.</summary>
    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errors = ExtractErrors(content);
            var summary = errors.Count > 0 ? string.Join("; ", errors) : response.ReasonPhrase ?? "Unknown error";
            throw new MaxioApiException($"Maxio API call failed ({(int)response.StatusCode}): {summary}", (int)response.StatusCode, errors);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException($"Could not parse the Maxio response: {ex.Message}", (int)response.StatusCode);
        }
    }

    /// <summary>Best-effort extraction of Maxio error strings, which may be an array or an object of messages.</summary>
    private static IReadOnlyList<string> ExtractErrors(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("errors", out var errors))
            {
                return Array.Empty<string>();
            }

            return errors.ValueKind switch
            {
                JsonValueKind.Array => errors.EnumerateArray()
                    .Select(e => e.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList(),
                JsonValueKind.Object => errors.EnumerateObject()
                    .Select(p => $"{p.Name}: {p.Value}")
                    .ToList(),
                JsonValueKind.String => new List<string> { errors.GetString()! },
                _ => Array.Empty<string>()
            };
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static SubscriptionPlan MapPlan(ProductDto product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        ProductId = product.Id,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
        RequireCreditCard = product.RequireCreditCard
    };

    private static MaxioCustomer MapCustomer(CustomerDto customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference,
        Email = customer.Email,
        FirstName = customer.FirstName,
        LastName = customer.LastName
    };

    private static SubscriptionSummary MapSubscription(SubscriptionDto subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        ProductPriceInCents = subscription.ProductPriceInCents,
        IntervalUnit = subscription.Product?.IntervalUnit,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CreatedAt = subscription.CreatedAt
    };

    // ----- Wire DTOs (snake_case handled by the shared JsonSerializerOptions) -----

    private sealed class ProductEnvelope
    {
        public ProductDto? Product { get; set; }
    }

    private sealed class ProductDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Handle { get; set; }
        public string? Description { get; set; }
        public long PriceInCents { get; set; }
        public int Interval { get; set; }
        public string? IntervalUnit { get; set; }
        public bool RequireCreditCard { get; set; }
        public DateTimeOffset? ArchivedAt { get; set; }
    }

    private sealed class CustomerEnvelope
    {
        public CustomerDto? Customer { get; set; }
    }

    private sealed class CustomerDto
    {
        public int Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Reference { get; set; }
    }

    private sealed class CreateSubscriptionEnvelope
    {
        public CreateSubscriptionDto Subscription { get; set; } = new();

        // Sits alongside (not inside) the subscription object per Maxio's duplicate-prevention docs.
        public string? UniquenessToken { get; set; }
    }

    private sealed class CreateSubscriptionDto
    {
        public string ProductHandle { get; set; } = string.Empty;
        public string CustomerReference { get; set; } = string.Empty;
        public string? PaymentCollectionMethod { get; set; }
    }

    private sealed class SubscriptionEnvelope
    {
        public SubscriptionDto? Subscription { get; set; }
    }

    private sealed class SubscriptionDto
    {
        public int Id { get; set; }
        public string? State { get; set; }
        public long ProductPriceInCents { get; set; }
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        public DateTimeOffset? NextAssessmentAt { get; set; }
        public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public ProductDto? Product { get; set; }
    }
}
