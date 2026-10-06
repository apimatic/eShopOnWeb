using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Integration with Maxio Advanced Billing as the billing system of record.
/// Idempotency: the Maxio customer is keyed on the eShopOnWeb user's email via the customer
/// "reference" field, and each subscription is keyed on a "eshop-sub:{email}:{plan-handle}"
/// reference, so repeated calls never create duplicate customers or subscriptions.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    public const string HttpClientName = "Maxio";

    private const string SubscriptionReferencePrefix = "eshop-sub";
    private const int SubscriptionPageSize = 100;
    private static readonly TimeSpan PlansCacheDuration = TimeSpan.FromSeconds(60);

    /// <summary>Subscription states that mean the customer is still enrolled.</summary>
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "on_hold", "past_due", "unpaid", "dunning"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        IHttpClientFactory httpClientFactory,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        return await TranslateAsync(async () =>
        {
            var products = await GetFamilyProductsAsync(cancellationToken);
            return products
                .Where(p => p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
                .Select(MapToPlan)
                .ToList() as IReadOnlyList<SubscriptionPlan>;
        });
    }

    public async Task<SubscriptionDetails> SubscribeAsync(string userEmail, string planHandle, CancellationToken cancellationToken = default)
    {
        GuardUserEmail(userEmail);
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));
        EnsureConfigured();

        return await TranslateAsync(async () =>
        {
            var planHandleValue = planHandle.Trim();
            var products = await GetFamilyProductsAsync(cancellationToken);
            var product = products.FirstOrDefault(p =>
                string.Equals(p.Handle, planHandleValue, StringComparison.OrdinalIgnoreCase));
            if (product is null || product.ArchivedAt is not null)
            {
                throw new BillingException($"Subscription plan '{planHandleValue}' was not found.", (int)HttpStatusCode.NotFound);
            }

            var customerId = await EnsureCustomerAsync(userEmail, cancellationToken);

            // Idempotency: a subscription already exists for this user + plan — return it
            // instead of creating a duplicate (covers double-clicks and retries).
            // NOTE: verified against the sandbox that GET /subscriptions.json does not honor a
            // "reference" query filter, so existing subscriptions are found via the customer's
            // filtered list (customer_id) and matched on the subscription reference field.
            var reference = BuildSubscriptionReference(userEmail, planHandleValue);
            var existing = await FindLiveSubscriptionAsync(customerId, reference, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Reusing existing Maxio subscription {SubscriptionId} for {UserEmail} on {PlanHandle}",
                    existing.Id, userEmail, planHandleValue);
                return MapToDetails(existing, product);
            }

            var request = new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscriptionFields
                {
                    ProductId = product.Id,
                    CustomerId = customerId,
                    Reference = reference
                }
            };

            var created = await SendAsync<MaxioSubscriptionEnvelope>(
                HttpMethod.Post, "/subscriptions.json", request, cancellationToken);

            var subscription = created?.Subscription
                ?? throw new MaxioApiException((int)HttpStatusCode.BadGateway, "Maxio returned an empty subscription response.");

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for {UserEmail} on {PlanHandle}",
                subscription.Id, userEmail, planHandleValue);

            return MapToDetails(subscription, product);
        });
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsForUserAsync(string userEmail, CancellationToken cancellationToken = default)
    {
        GuardUserEmail(userEmail);
        EnsureConfigured();

        return await TranslateAsync(async () =>
        {
            var customer = await TryLookupCustomerAsync(userEmail, cancellationToken);
            if (customer is null)
            {
                return Array.Empty<SubscriptionDetails>();
            }

            var summarySubscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var result = new List<SubscriptionDetails>(summarySubscriptions.Count);
            foreach (var summary in summarySubscriptions)
            {
                // List results are condensed (no product info), so read each subscription in full.
                var full = await SendAsync<MaxioSubscriptionEnvelope>(
                    HttpMethod.Get, $"/subscriptions/{summary.Id}.json", null, cancellationToken);
                var subscription = full?.Subscription;
                if (subscription is null)
                {
                    continue;
                }
                result.Add(MapToDetails(subscription, subscription.Product));
            }

            return result.OrderByDescending(s => s.CreatedAt).ToList() as IReadOnlyList<SubscriptionDetails>;
        });
    }

    /// <summary>
    /// Translates Maxio API failures into <see cref="BillingException"/> so the API layer can
    /// map them onto meaningful HTTP statuses instead of a 500.
    /// </summary>
    private async Task<T> TranslateAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (MaxioApiException ex)
        {
            _logger.LogWarning("Maxio API error ({StatusCode}): {Message}", ex.StatusCode, ex.Message);
            throw new BillingException(ex.Message, MapToPublicStatusCode(ex.StatusCode));
        }
    }

    private static int? MapToPublicStatusCode(int statusCode) =>
        statusCode switch
        {
            (int)HttpStatusCode.NotFound => (int)HttpStatusCode.NotFound,
            (int)HttpStatusCode.UnprocessableEntity => (int)HttpStatusCode.UnprocessableEntity,
            _ => null
        };

    private static SubscriptionPlan MapToPlan(MaxioProductDto product) =>
        new(
            product.Handle!,
            product.Name ?? product.Handle!,
            product.Description,
            CentsToPrice(product.PriceInCents),
            product.Interval ?? 1,
            product.IntervalUnit ?? "month");

    private static SubscriptionDetails MapToDetails(MaxioSubscriptionDto subscription, MaxioProductDto? product) =>
        new(
            subscription.Id,
            product?.Handle ?? subscription.Reference ?? string.Empty,
            product?.Name ?? "Subscription",
            CentsToPrice(product?.PriceInCents),
            subscription.State ?? string.Empty,
            subscription.NextAssessmentAt,
            subscription.CreatedAt);

    private static decimal CentsToPrice(long? cents) => (cents ?? 0) / 100m;

    private static string BuildSubscriptionReference(string userEmail, string planHandle) =>
        $"{SubscriptionReferencePrefix}:{userEmail.Trim()}:{planHandle.Trim()}".ToLowerInvariant();

    private static readonly Regex UserEmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private static void GuardUserEmail(string userEmail)
    {
        Guard.Against.NullOrEmpty(userEmail, nameof(userEmail));
        if (!UserEmailRegex.IsMatch(userEmail))
        {
            throw new BillingException("A valid authenticated user email is required to manage subscriptions.");
        }
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException(
                $"Maxio billing is not configured. Set {MaxioOptions.CONFIG_NAME}:ApiKey and "
                + $"{MaxioOptions.CONFIG_NAME}:ProductFamilyHandle (via user-secrets or environment variables).");
        }
    }

    private async Task<IReadOnlyList<MaxioProductDto>> GetFamilyProductsAsync(CancellationToken cancellationToken)
    {
        return await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PlansCacheDuration;

            var familyHandle = _options.ProductFamilyHandle.Trim();
            var family = await SendAsync<MaxioProductFamilyEnvelope>(
                HttpMethod.Get, $"/product_families/lookup.json?handle={Uri.EscapeDataString(familyHandle)}",
                null, cancellationToken);
            var familyId = family?.ProductFamily?.Id
                ?? throw new BillingException(
                    $"Product family '{familyHandle}' was not found in the billing system.",
                    (int)HttpStatusCode.InternalServerError);

            var products = await SendAsync<List<MaxioProductEnvelope>>(
                HttpMethod.Get, $"/product_families/{familyId}/products.json", null, cancellationToken);

            return products?
                .Where(e => e.Product is not null)
                .Select(e => e.Product!)
                .ToList()
                ?? new List<MaxioProductDto>();
        }) ?? new List<MaxioProductDto>();
    }

    private static readonly object CacheKey = new();

    private async Task<int> EnsureCustomerAsync(string userEmail, CancellationToken cancellationToken)
    {
        var existing = await TryLookupCustomerAsync(userEmail, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var localPart = userEmail.Split('@')[0];
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerFields
            {
                FirstName = localPart,
                LastName = "Customer",
                Email = userEmail,
                Reference = userEmail
            }
        };

        try
        {
            var created = await SendAsync<MaxioCustomerEnvelope>(
                HttpMethod.Post, "/customers.json", request, cancellationToken);
            var customer = created?.Customer
                ?? throw new MaxioApiException((int)HttpStatusCode.BadGateway, "Maxio returned an empty customer response.");
            _logger.LogInformation("Created Maxio customer {CustomerId} for {UserEmail}", customer.Id, userEmail);
            return customer.Id;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request may have created the customer first — resolve the winner.
            var raced = await TryLookupCustomerAsync(userEmail, cancellationToken);
            if (raced is not null)
            {
                return raced.Id;
            }
            throw new BillingException(ex.Message);
        }
    }

    private async Task<MaxioCustomerDto?> TryLookupCustomerAsync(string userEmail, CancellationToken cancellationToken)
    {
        var path = $"/customers/lookup.json?reference={Uri.EscapeDataString(userEmail)}";
        var response = await SendRawAsync(HttpMethod.Get, path, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer;
    }

    private async Task<MaxioSubscriptionDto?> FindLiveSubscriptionAsync(int customerId, string reference, CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Reference, reference, StringComparison.OrdinalIgnoreCase) && IsLive(s.State));
    }

    private async Task<List<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var result = new List<MaxioSubscriptionDto>();
        var page = 1;
        while (true)
        {
            var path = $"/subscriptions.json?customer_id={customerId}&page={page}&per_page={SubscriptionPageSize}";
            var response = await SendRawAsync(HttpMethod.Get, path, null, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                break;
            }
            await EnsureSuccessAsync(response, cancellationToken);
            var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionEnvelope>>(JsonOptions, cancellationToken)
                ?? new List<MaxioSubscriptionEnvelope>();
            var subscriptions = envelopes.Select(e => e.Subscription).Where(s => s is not null).Select(s => s!).ToList();
            result.AddRange(subscriptions);
            if (subscriptions.Count < SubscriptionPageSize)
            {
                break;
            }
            page++;
        }
        return result;
    }

    private static bool IsLive(string? state) =>
        !string.IsNullOrEmpty(state) && LiveSubscriptionStates.Contains(state);

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken) where T : class
    {
        var response = await SendRawAsync(method, path, body, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, body.GetType(), JsonOptions),
                Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        return response;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = ExtractErrorMessages(body);
        if (string.IsNullOrEmpty(message))
        {
            message = $"Maxio request failed with status {(int)response.StatusCode}.";
        }
        throw new MaxioApiException((int)response.StatusCode, message);
    }

    /// <summary>
    /// Maxio reports validation failures as {"errors": ["msg", ...]} or {"errors": {"field": ["msg", ...]}}.
    /// </summary>
    private static string? ExtractErrorMessages(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return null;
            }

            var messages = new List<string>();
            switch (errors.ValueKind)
            {
                case JsonValueKind.Array:
                    messages.AddRange(errors.EnumerateArray().Select(e => e.ToString()));
                    break;
                case JsonValueKind.Object:
                    messages.AddRange(errors.EnumerateObject()
                        .SelectMany(property => property.Value.ValueKind == JsonValueKind.Array
                            ? property.Value.EnumerateArray().Select(e => e.ToString())
                            : new[] { property.Value.ToString() }));
                    break;
                default:
                    messages.Add(errors.ToString());
                    break;
            }

            return messages.Count > 0 ? string.Join("; ", messages) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
