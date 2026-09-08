using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing API (Basic auth over TLS: API key as username,
/// literal "X" as password; JSON resources under /{resource}.json).
/// </summary>
public class MaxioBillingClient : IMaxioBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const int MaxGetAttempts = 3;
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(2);

    public MaxioBillingClient(HttpClient httpClient, IAppLogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(productFamilyHandle, nameof(productFamilyHandle));

        var requestUri = $"product_families/{Uri.EscapeDataString($"handle:{productFamilyHandle}")}/products.json?per_page=200";
        using var document = await GetWithRetryAsync(requestUri, cancellationToken);

        var plans = new List<MaxioPlan>();
        foreach (var element in EnumerateWrappedItems(document.RootElement, "product"))
        {
            var dto = element.Deserialize<ProductDto>(JsonOptions);
            if (dto is null || dto.ArchivedAt is not null)
            {
                continue;
            }

            plans.Add(new MaxioPlan
            {
                Id = dto.Id,
                Handle = dto.Handle,
                Name = dto.Name,
                Description = dto.Description,
                PriceInCents = dto.PriceInCents,
                Interval = dto.Interval,
                IntervalUnit = dto.IntervalUnit,
                RequiresPaymentMethod = dto.RequireCreditCard,
                ProductFamilyHandle = dto.ProductFamily?.Handle ?? productFamilyHandle,
            });
        }

        return plans;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(reference, nameof(reference));

        var requestUri = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        using var document = await ReadAsync(response, requestUri, cancellationToken);
        var wrapper = document.RootElement.Deserialize<CustomerWrapperDto>(JsonOptions);
        return wrapper?.Customer is null ? null : ToCustomer(wrapper.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(SubscriberProfile profile, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(profile);

        var payload = new CustomerWrapperDto
        {
            Customer = new CustomerDto
            {
                FirstName = profile.FirstName,
                LastName = profile.LastName,
                Email = profile.Email,
                Reference = profile.Reference,
            },
        };

        using var document = await PostJsonAsync("customers.json", payload, cancellationToken);
        var wrapper = document.RootElement.Deserialize<CustomerWrapperDto>(JsonOptions)
            ?? throw new MaxioIntegrationException("Maxio returned an unexpected create-customer response.");

        return ToCustomer(wrapper.Customer
            ?? throw new MaxioIntegrationException("Maxio returned an unexpected create-customer response."));
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var requestUri = $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json?per_page=200";
        using var document = await GetWithRetryAsync(requestUri, cancellationToken);

        var subscriptions = new List<MaxioSubscription>();
        foreach (var element in EnumerateWrappedItems(document.RootElement, "subscription"))
        {
            var dto = element.Deserialize<SubscriptionDto>(JsonOptions);
            if (dto is not null)
            {
                subscriptions.Add(ToSubscription(dto, customerId));
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string planHandle, string? subscriptionReference, string uniquenessToken, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Guard.Against.NullOrWhiteSpace(uniquenessToken, nameof(uniquenessToken));

        var payload = new CreateSubscriptionRequestDto
        {
            Subscription = new SubscriptionCreateDto
            {
                CustomerId = customerId,
                ProductHandle = planHandle,
                Reference = subscriptionReference,
            },
            UniquenessToken = uniquenessToken,
        };

        using var document = await PostJsonAsync("subscriptions.json", payload, cancellationToken);
        var dto = document.RootElement.Deserialize<SubscriptionWrapperDto>(JsonOptions)?.Subscription
            ?? throw new MaxioIntegrationException("Maxio returned an unexpected create-subscription response.");

        return ToSubscription(dto, customerId);
    }

    private async Task<JsonDocument> PostJsonAsync(string relativeUri, object payload, CancellationToken cancellationToken)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(relativeUri, content, cancellationToken);
        return await ReadAsync(response, relativeUri, cancellationToken);
    }

    /// <summary>
    /// GET with bounded retry for transient failures (429/5xx/transport). GETs are read-only,
    /// so retrying is safe. Write endpoints are NOT retried here; they rely on Maxio
    /// uniqueness tokens to make caller-level retries duplicate-safe.
    /// </summary>
    private async Task<JsonDocument> GetWithRetryAsync(string relativeUri, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await _httpClient.GetAsync(relativeUri, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return await ReadSuccessAsync(response, cancellationToken);
                }

                if (IsTransient(response.StatusCode) && attempt < MaxGetAttempts)
                {
                    _logger.LogWarning("Maxio GET {Uri} returned {Status} (attempt {Attempt}/{Max}); retrying.",
                        relativeUri, (int)response.StatusCode, attempt, MaxGetAttempts);
                    await Task.Delay(RetryBaseDelay * attempt, cancellationToken);
                    continue;
                }

                await ThrowForFailedResponseAsync(response, relativeUri, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       && attempt < MaxGetAttempts
                                       && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Transient error calling Maxio GET {Uri} (attempt {Attempt}/{Max}): {Message}",
                    relativeUri, attempt, MaxGetAttempts, ex.Message);
                await Task.Delay(RetryBaseDelay * attempt, cancellationToken);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private async Task<JsonDocument> ReadAsync(HttpResponseMessage response, string relativeUri, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return await ReadSuccessAsync(response, cancellationToken);
        }

        await ThrowForFailedResponseAsync(response, relativeUri, cancellationToken);
        throw new InvalidOperationException("Unreachable.");
    }

    private static async Task<JsonDocument> ReadSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    private async Task ThrowForFailedResponseAsync(HttpResponseMessage response, string relativeUri, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = TryParseErrors(body);

        _logger.LogWarning("Maxio API error on {Uri}: {Status} {Errors}", relativeUri, (int)response.StatusCode, string.Join("; ", errors));

        throw new MaxioApiException(response.StatusCode, errors);
    }

    private static IReadOnlyList<string> TryParseErrors(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return errorsElement.ValueKind switch
                {
                    JsonValueKind.Array => errorsElement.EnumerateArray()
                        .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.ToString())
                        .ToList(),
                    JsonValueKind.String => new List<string> { errorsElement.GetString() ?? string.Empty },
                    JsonValueKind.Object => errorsElement.EnumerateObject()
                        .SelectMany(p => p.Value.ValueKind == JsonValueKind.Array
                            ? p.Value.EnumerateArray().Select(v => $"{p.Name} {v.GetString()}")
                            : new[] { $"{p.Name} {p.Value}" })
                        .ToList(),
                    _ => new List<string> { errorsElement.ToString() },
                };
            }
        }
        catch (JsonException)
        {
            // fall through to the raw body
        }

        return string.IsNullOrWhiteSpace(body)
            ? Array.Empty<string>()
            : new[] { body.Length > 512 ? body[..512] : body };
    }

    private static IEnumerable<JsonElement> EnumerateWrappedItems(JsonElement root, string wrapperProperty)
    {
        // Maxio list endpoints return a JSON array of single-property wrapper objects,
        // e.g. [ { "product": { ... } }, { "product": { ... } } ].
        if (root.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty(wrapperProperty, out var inner)
                && inner.ValueKind == JsonValueKind.Object)
            {
                yield return inner;
            }
        }
    }

    private static MaxioCustomer ToCustomer(CustomerDto dto) => new()
    {
        Id = dto.Id,
        Reference = dto.Reference,
        Email = dto.Email,
    };

    private static MaxioSubscription ToSubscription(SubscriptionDto dto, long fallbackCustomerId) => new()
    {
        Id = dto.Id,
        CustomerId = dto.CustomerId ?? fallbackCustomerId,
        State = dto.State,
        PlanHandle = dto.Product?.Handle,
        PlanName = dto.Product?.Name ?? dto.Plan?.Name ?? string.Empty,
        PriceInCents = dto.Product?.PriceInCents ?? dto.ProductPriceInCents,
        Interval = dto.Product?.Interval ?? 0,
        IntervalUnit = dto.Product?.IntervalUnit,
        CurrentPeriodEndsAt = dto.CurrentPeriodEndsAt,
        NextAssessmentAt = dto.NextAssessmentAt,
        CreatedAt = dto.CreatedAt,
    };

    #region Wire DTOs

    private sealed class ProductDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("handle")] public string? Handle { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("price_in_cents")] public long PriceInCents { get; set; }
        [JsonPropertyName("interval")] public int Interval { get; set; }
        [JsonPropertyName("interval_unit")] public string IntervalUnit { get; set; } = "month";
        [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; set; }
        [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; set; }
        [JsonPropertyName("product_family")] public ProductFamilyDto? ProductFamily { get; set; }
    }

    private sealed class ProductFamilyDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("handle")] public string? Handle { get; set; }
    }

    private sealed class CustomerWrapperDto
    {
        [JsonPropertyName("customer")] public CustomerDto? Customer { get; set; }
    }

    private sealed class CustomerDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("first_name")] public string? FirstName { get; set; }
        [JsonPropertyName("last_name")] public string? LastName { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("reference")] public string? Reference { get; set; }
    }

    private sealed class CreateSubscriptionRequestDto
    {
        [JsonPropertyName("subscription")] public SubscriptionCreateDto Subscription { get; set; } = new();
        [JsonPropertyName("uniqueness_token")] public string UniquenessToken { get; set; } = string.Empty;
    }

    private sealed class SubscriptionCreateDto
    {
        [JsonPropertyName("customer_id")] public long CustomerId { get; set; }
        [JsonPropertyName("product_handle")] public string ProductHandle { get; set; } = string.Empty;
        [JsonPropertyName("reference")] public string? Reference { get; set; }
    }

    private sealed class SubscriptionWrapperDto
    {
        [JsonPropertyName("subscription")] public SubscriptionDto? Subscription { get; set; }
    }

    private sealed class SubscriptionDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("customer_id")] public long? CustomerId { get; set; }
        [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
        [JsonPropertyName("product_price_in_cents")] public long ProductPriceInCents { get; set; }
        [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("product")] public SubscriptionProductDto? Product { get; set; }
        [JsonPropertyName("plan")] public SubscriptionProductDto? Plan { get; set; }
    }

    private sealed class SubscriptionProductDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("handle")] public string? Handle { get; set; }
        [JsonPropertyName("price_in_cents")] public long PriceInCents { get; set; }
        [JsonPropertyName("interval")] public int Interval { get; set; }
        [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; set; }
    }

    #endregion
}
