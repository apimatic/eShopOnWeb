using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="IMaxioGateway"/> implementation backed by the Maxio Advanced Billing REST API.
/// Uses a typed <see cref="HttpClient"/> (Basic auth + base address configured in DI).
/// </summary>
public class MaxioGateway : IMaxioGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private const int MaxAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioGateway> _logger;

    public MaxioGateway(HttpClient httpClient, IOptions<MaxioSettings> settings, IAppLogger<MaxioGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = Uri.EscapeDataString(_settings.ProductFamilyHandle);
        var url = $"product_families/handle:{familyHandle}/products.json?per_page=200";

        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var products = await ReadJsonAsync<List<ProductEnvelope>>(response, cancellationToken) ?? new();
        return products
            .Select(p => p.Product)
            .Where(p => p is not null && p!.ArchivedAt is null)
            .Select(p => MapPlan(p!))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await ReadJsonAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer is { } dto ? MapCustomer(dto) : null;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CustomerRegistration registration, CancellationToken cancellationToken = default)
    {
        var payload = new CreateCustomerRequest
        {
            Customer = new CustomerAttributes
            {
                FirstName = registration.FirstName,
                LastName = registration.LastName,
                Email = registration.Email,
                Reference = registration.Reference,
            },
        };

        using var response = await SendAsync(
            () => JsonRequest(HttpMethod.Post, "customers.json", payload), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await ReadJsonAsync<CustomerEnvelope>(response, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioApiException((int)response.StatusCode, new[] { "Create customer returned an empty body." });
        }

        return MapCustomer(envelope.Customer);
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(NewSubscription subscription, CancellationToken cancellationToken = default)
    {
        var payload = new CreateSubscriptionRequest
        {
            Subscription = new SubscriptionAttributes
            {
                CustomerId = subscription.CustomerId,
                ProductHandle = subscription.ProductHandle,
                PaymentCollectionMethod = string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod)
                    ? null
                    : _settings.PaymentCollectionMethod,
            },
            UniquenessToken = subscription.UniquenessToken,
        };

        using var response = await SendAsync(
            () => JsonRequest(HttpMethod.Post, "subscriptions.json", payload), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await ReadJsonAsync<SubscriptionEnvelope>(response, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException((int)response.StatusCode, new[] { "Create subscription returned an empty body." });
        }

        return MapSubscription(envelope.Subscription);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var url = $"customers/{customerId}/subscriptions.json?per_page=200";

        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var subscriptions = await ReadJsonAsync<List<SubscriptionEnvelope>>(response, cancellationToken) ?? new();
        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    // --- HTTP plumbing -----------------------------------------------------------------------

    private static HttpRequestMessage JsonRequest(HttpMethod method, string url, object body)
        => new(method, url) { Content = JsonContent.Create(body, options: JsonOptions) };

    /// <summary>
    /// Sends a request, retrying on transient failures (429 and 5xx gateway errors). The request
    /// is rebuilt on each attempt because an <see cref="HttpRequestMessage"/> cannot be reused.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            response?.Dispose();
            using var request = requestFactory();
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!IsTransient(response.StatusCode) || attempt == MaxAttempts)
            {
                return response;
            }

            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)); // 1s, 2s
            _logger.LogWarning(
                "Maxio returned transient status {0} for {1}; retrying in {2}s (attempt {3}/{4}).",
                (int)response.StatusCode, request.RequestUri?.ToString() ?? "(unknown)", delay.TotalSeconds, attempt, MaxAttempts);
            await Task.Delay(delay, cancellationToken);
        }

        return response!;
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests
           || statusCode == HttpStatusCode.BadGateway
           || statusCode == HttpStatusCode.ServiceUnavailable
           || statusCode == HttpStatusCode.GatewayTimeout;

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ParseErrors(body);
        throw new MaxioApiException((int)response.StatusCode, errors, body);
    }

    /// <summary>
    /// Extracts human-readable messages from a Maxio error body, which may be
    /// <c>{"errors":[...]}</c>, <c>{"errors":{field:"..."}}</c>, or <c>{"error":"..."}</c>.
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors))
            {
                return FlattenErrorElement(errors);
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var single) &&
                single.ValueKind == JsonValueKind.String)
            {
                return new[] { single.GetString() ?? string.Empty };
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall through to returning the raw body.
        }

        return new[] { body.Trim() };
    }

    private static IReadOnlyList<string> FlattenErrorElement(JsonElement errors)
    {
        var messages = new List<string>();
        switch (errors.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in errors.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        messages.Add(item.GetString() ?? string.Empty);
                    }
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        messages.Add($"{property.Name}: {property.Value.GetString()}");
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            messages.Add($"{property.Name}: {item.GetString()}");
                        }
                    }
                }

                break;
            case JsonValueKind.String:
                messages.Add(errors.GetString() ?? string.Empty);
                break;
        }

        return messages.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
    }

    // --- Mapping -----------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(ProductDto dto) => new()
    {
        Id = dto.Id,
        Handle = dto.Handle ?? string.Empty,
        Name = dto.Name ?? string.Empty,
        Description = dto.Description,
        PriceInCents = dto.PriceInCents,
        Interval = dto.Interval,
        IntervalUnit = dto.IntervalUnit ?? string.Empty,
        RequireCreditCard = dto.RequireCreditCard,
    };

    private static MaxioCustomer MapCustomer(CustomerDto dto) => new()
    {
        Id = dto.Id,
        Reference = dto.Reference,
        Email = dto.Email ?? string.Empty,
        FirstName = dto.FirstName ?? string.Empty,
        LastName = dto.LastName ?? string.Empty,
    };

    private static CustomerSubscription MapSubscription(SubscriptionDto dto) => new()
    {
        Id = dto.Id,
        State = dto.State ?? string.Empty,
        ProductHandle = dto.Product?.Handle ?? string.Empty,
        ProductName = dto.Product?.Name ?? string.Empty,
        ProductPriceInCents = dto.Product?.PriceInCents ?? 0,
        ProductInterval = dto.Product?.Interval ?? 0,
        ProductIntervalUnit = dto.Product?.IntervalUnit ?? string.Empty,
        CurrentPeriodStartedAt = dto.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = dto.CurrentPeriodEndsAt,
        NextAssessmentAt = dto.NextAssessmentAt,
        CreatedAt = dto.CreatedAt,
    };
}
