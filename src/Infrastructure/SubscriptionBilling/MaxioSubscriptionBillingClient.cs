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
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.SubscriptionBilling;

/// <summary>
/// Maxio Advanced Billing (Billing API) adapter.
///
/// Documented essentials this implementation relies on:
/// - Requests go to https://{subdomain}.chargify.com/{resource}.json with HTTP Basic
///   authentication over TLS (API key as username, "X" as password).
/// - Customer "reference" is unique site-side, which anchors idempotent provisioning.
/// - Any POST may carry a top-level "uniqueness_token"; a replay within 60 minutes is
///   rejected with 409 instead of creating a duplicate.
/// - The site throttles by concurrency (max 4 in-flight calls) and answers with 429.
/// - Errors are JSON of the shape { "errors": ["..."] } (sometimes a single string).
/// </summary>
public class MaxioSubscriptionBillingClient : ISubscriptionBillingClient
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan MaxRetryBackoff = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly HttpClient _httpClient;
    private readonly SubscriptionBillingSettings _settings;

    public MaxioSubscriptionBillingClient(HttpClient httpClient, IOptions<SubscriptionBillingSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        RequireProductFamilyHandle();

        var familyHandle = Uri.EscapeDataString(_settings.ProductFamilyHandle!.Trim());
        var (status, body) = await SendAsync(HttpMethod.Get, $"product_families/handle:{familyHandle}/products.json", null, cancellationToken);
        GuardSuccess(status, body, "list plans");

        var items = JsonSerializer.Deserialize<List<MaxioWireModels.ProductResponse>>(body ?? "[]", ResponseJsonOptions)
                    ?? new List<MaxioWireModels.ProductResponse>();

        return items
            .Where(item => item.Product is not null && !string.IsNullOrWhiteSpace(item.Product.Handle) && item.Product.ArchivedAt is null)
            .Select(item => new BillingPlan
            {
                Id = item.Product!.Id,
                Handle = item.Product.Handle!,
                Name = item.Product.Name ?? item.Product.Handle!,
                Description = item.Product.Description,
                PriceInCents = item.Product.PriceInCents,
                Interval = item.Product.Interval > 0 ? item.Product.Interval : 1,
                IntervalUnit = string.IsNullOrWhiteSpace(item.Product.IntervalUnit) ? "month" : item.Product.IntervalUnit!,
                RequiresPaymentProfile = item.Product.RequireCreditCard,
            })
            .ToList();
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        var (status, body) = await SendAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken);
        if (status == HttpStatusCode.NotFound)
        {
            return null;
        }

        GuardSuccess(status, body, "find customer");

        var response = JsonSerializer.Deserialize<MaxioWireModels.CustomerResponse>(body ?? "{}", ResponseJsonOptions);
        return response?.Customer is null ? null : MapCustomer(response.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentException.ThrowIfNullOrWhiteSpace(customer.Reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(customer.Email);

        var payload = new
        {
            Customer = new
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference,
            },
            UniquenessToken = Guid.NewGuid().ToString("N"),
        };

        var (status, body) = await SendAsync(HttpMethod.Post, "customers.json", payload, cancellationToken);
        GuardSuccess(status, body, "create customer");

        var response = JsonSerializer.Deserialize<MaxioWireModels.CustomerResponse>(body ?? "{}", ResponseJsonOptions);
        if (response?.Customer is null)
        {
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Unavailable,
                "The billing provider accepted the customer creation but returned no customer record.");
        }

        return MapCustomer(response.Customer);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(NewBillingSubscription subscription, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription.ProductHandle);

        var payload = new
        {
            Subscription = new
            {
                ProductHandle = subscription.ProductHandle,
                CustomerId = subscription.CustomerId,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            },
            UniquenessToken = string.IsNullOrWhiteSpace(subscription.IdempotencyKey)
                ? Guid.NewGuid().ToString("N")
                : subscription.IdempotencyKey!.Trim(),
        };

        var (status, body) = await SendAsync(HttpMethod.Post, "subscriptions.json", payload, cancellationToken);
        GuardSuccess(status, body, "create subscription");

        var response = JsonSerializer.Deserialize<MaxioWireModels.SubscriptionResponse>(body ?? "{}", ResponseJsonOptions);
        if (response?.Subscription is null)
        {
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Unavailable,
                "The billing provider accepted the subscription request but returned no subscription record.");
        }

        return MapSubscription(response.Subscription);
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var (status, body) = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", null, cancellationToken);
        if (status == HttpStatusCode.NotFound)
        {
            return Array.Empty<BillingSubscription>();
        }

        GuardSuccess(status, body, "list customer subscriptions");

        var items = JsonSerializer.Deserialize<List<MaxioWireModels.SubscriptionResponse>>(body ?? "[]", ResponseJsonOptions)
                    ?? new List<MaxioWireModels.SubscriptionResponse>();

        return items
            .Where(item => item.Subscription is not null)
            .Select(item => MapSubscription(item.Subscription!))
            .ToList();
    }

    private void RequireProductFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Configuration,
                "Maxio billing is not configured. Set the Maxio:ProductFamilyHandle configuration key.");
        }
    }

    private static BillingCustomer MapCustomer(MaxioWireModels.WireCustomer customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference,
        FirstName = customer.FirstName ?? string.Empty,
        LastName = customer.LastName ?? string.Empty,
        Email = customer.Email ?? string.Empty,
    };

    private static BillingSubscription MapSubscription(MaxioWireModels.WireSubscription subscription)
    {
        var product = subscription.Product;
        var priceInCents = subscription.ProductPriceInCents > 0
            ? subscription.ProductPriceInCents
            : product?.PriceInCents ?? 0;

        return new BillingSubscription
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            CustomerId = subscription.Customer?.Id ?? 0,
            PlanHandle = product?.Handle,
            PlanName = product?.Name ?? product?.Handle ?? "Unknown plan",
            PriceInCents = priceInCents,
            Interval = product is { Interval: > 0 } ? product.Interval : 1,
            IntervalUnit = string.IsNullOrWhiteSpace(product?.IntervalUnit) ? "month" : product!.IntervalUnit!,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod ?? string.Empty,
            CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt ?? DateTimeOffset.MinValue,
        };
    }

    /// <summary>
    /// Sends a request with bounded retries. GETs retry transient 429/5xx and transport
    /// failures; POSTs only retry 429 (which the provider returns before processing the
    /// request), so a never-confirmed write is never blindly replayed. Retried POST bodies
    /// keep the same uniqueness token, making any replay safe on the provider side.
    /// </summary>
    private async Task<(HttpStatusCode StatusCode, string? Body)> SendAsync(HttpMethod method, string pathAndQuery, object? jsonPayload, CancellationToken cancellationToken)
    {
        var isSafeMethod = HttpMethod.Get.Equals(method);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, pathAndQuery);
                if (jsonPayload is not null)
                {
                    request.Content = new StringContent(JsonSerializer.Serialize(jsonPayload, RequestJsonOptions), Encoding.UTF8, "application/json");
                }

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);

                if (ShouldRetry(method, response.StatusCode, attempt) && await WaitForRetryAsync(response, attempt, cancellationToken))
                {
                    continue;
                }

                var body = await SafeReadBodyAsync(response, cancellationToken);
                return (response.StatusCode, body);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient timeout. Safe to repeat for GETs; ambiguous for POSTs, where
                // the caller can retry with the same IdempotencyKey instead.
                if (isSafeMethod && attempt < MaxAttempts && await WaitForRetryAsync(null, attempt, cancellationToken))
                {
                    continue;
                }

                throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Unavailable,
                    "The billing provider did not respond before the request timed out.");
            }
            catch (HttpRequestException ex)
            {
                if (isSafeMethod && attempt < MaxAttempts && await WaitForRetryAsync(null, attempt, cancellationToken))
                {
                    continue;
                }

                throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Unavailable,
                    "Could not reach the billing provider.", innerException: ex);
            }
        }
    }

    private static bool ShouldRetry(HttpMethod method, HttpStatusCode statusCode, int attempt)
    {
        if (attempt >= MaxAttempts || !IsRetryableStatus(statusCode))
        {
            return false;
        }

        // POSTs are only replayed after a 429, which the provider returns without processing.
        return HttpMethod.Get.Equals(method) || statusCode == HttpStatusCode.TooManyRequests;
    }

    private static bool IsRetryableStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    /// <summary>
    /// Honors the provider's Retry-After hint when present, otherwise exponential backoff.
    /// Returns false when waiting would exceed the remaining retry budget for the attempt.
    /// </summary>
    private static async Task<bool> WaitForRetryAsync(HttpResponseMessage? response, int attempt, CancellationToken cancellationToken)
    {
        if (attempt >= MaxAttempts)
        {
            return false;
        }

        var backoff = TimeSpan.FromMilliseconds(400 * Math.Pow(2, attempt - 1));
        var retryAfter = response?.Headers.RetryAfter?.Delta;
        if (retryAfter is { } hinted && hinted > TimeSpan.Zero)
        {
            backoff = hinted;
        }

        if (backoff > MaxRetryBackoff)
        {
            backoff = MaxRetryBackoff;
        }

        await Task.Delay(backoff, cancellationToken);
        return true;
    }

    private static async Task<string?> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static void GuardSuccess(HttpStatusCode statusCode, string? body, string context)
    {
        if ((int)statusCode is >= 200 and < 300)
        {
            return;
        }

        throw BuildFailureException(statusCode, body, context);
    }

    private static SubscriptionBillingException BuildFailureException(HttpStatusCode statusCode, string? body, string context)
    {
        var errors = ParseErrors(body);
        var kind = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => SubscriptionBillingErrorKind.Unauthorized,
            HttpStatusCode.NotFound => SubscriptionBillingErrorKind.ResourceNotFound,
            HttpStatusCode.Conflict => SubscriptionBillingErrorKind.Duplicate,
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => SubscriptionBillingErrorKind.Rejected,
            _ => SubscriptionBillingErrorKind.Unavailable,
        };

        var message = $"Maxio billing request failed ({context}, HTTP {(int)statusCode}).";
        if (errors.Count > 0)
        {
            message = $"{message} {string.Join(" ", errors)}";
        }

        return new SubscriptionBillingException(kind, message, errors);
    }

    /// <summary>
    /// Maxio error bodies are typically { "errors": ["..."] } but occasionally carry a
    /// single string, e.g. { "errors": "..." }. Anything unparseable is surfaced verbatim
    /// (truncated) so operators can see why a call failed.
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return new[] { Truncate(body) };
            }

            return errorsElement.ValueKind switch
            {
                JsonValueKind.Array => errorsElement.EnumerateArray()
                    .Select(static element => element.ValueKind == JsonValueKind.String ? element.GetString()! : Truncate(element.GetRawText()))
                    .Where(static message => !string.IsNullOrWhiteSpace(message))
                    .ToList(),
                JsonValueKind.String => new[] { errorsElement.GetString()! },
                _ => new[] { Truncate(body) },
            };
        }
        catch (JsonException)
        {
            return new[] { Truncate(body) };
        }
    }

    private static string Truncate(string value)
    {
        const int max = 300;
        return value.Length <= max ? value : value[..max] + "...";
    }
}
