using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Billing.Models;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Api;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Hand-written client for the Maxio Advanced Billing API, built strictly against the
/// OpenAPI specification in maxio-spec/openapi.yaml:
///   - server template            https://{site}.chargify.com (see MaxioSettings.ResolveBaseUrl)
///   - auth (BasicAuth)           username = API key, password = "x"
///   - List plans                 GET  /product_families/handle:{handle}/products.json
///   - Customer by reference      GET  /customers/lookup.json?reference=
///   - Create customer            POST /customers.json
///   - Subscription by reference  GET  /subscriptions/lookup.json?reference=
///   - Create subscription        POST /subscriptions.json
///   - Customer subscriptions     GET  /customers/{customer_id}/subscriptions.json
/// </summary>
public class MaxioBillingGateway : IMaxioBillingGateway
{
    private readonly HttpClient _httpClient;
    private readonly IAppLogger<MaxioBillingGateway> _logger;
    private readonly MaxioSettings _settings;

    public MaxioBillingGateway(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingGateway> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _settings = settings.Value;
        _settings.ValidateRequiredSettings();
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle!.Trim())}/products.json";

        using var response = await ReadAsync(path, cancellationToken);
        var envelopes = await ReadJsonAsync<List<ProductEnvelope>>(response, cancellationToken);

        return envelopes
            .Where(e => e.Product is not null)
            .Select(e => e.Product!)
            .Select(p => new SubscriptionPlan
            {
                Id = p.Id,
                Handle = p.Handle ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? "month",
                RequiresPaymentMethod = p.RequireCreditCard,
                IsArchived = p.ArchivedAt is not null,
                ArchivedAt = p.ArchivedAt
            })
            .ToList();
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        using var response = await ReadAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<CustomerEnvelope>(response, cancellationToken);

        return envelope.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default)
    {
        var body = new CreateCustomerRequestBody
        {
            Customer = new CreateCustomerRequestBody.CustomerWriteAttributes
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", body, Json.SerializeOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await ReadJsonAsync<CustomerEnvelope>(response, cancellationToken);
        if (envelope.Customer is null)
            throw new MaxioApiException((int)response.StatusCode, "Maxio customer-create response did not contain a customer object.");

        return MapCustomer(envelope.Customer);
    }

    public async Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";

        using var response = await ReadAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<SubscriptionEnvelope>(response, cancellationToken);

        return envelope.Subscription is null ? null : MapSubscription(envelope.Subscription);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(NewBillingSubscription subscription, CancellationToken cancellationToken = default)
    {
        var body = new CreateSubscriptionRequestBody
        {
            Subscription = new CreateSubscriptionRequestBody.SubscriptionWriteAttributes
            {
                ProductHandle = subscription.PlanHandle,
                CustomerReference = subscription.CustomerReference,
                Reference = subscription.Reference,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", body, Json.SerializeOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await ReadJsonAsync<SubscriptionEnvelope>(response, cancellationToken);
        if (envelope.Subscription is null)
            throw new MaxioApiException((int)response.StatusCode, "Maxio subscription-create response did not contain a subscription object.");

        return MapSubscription(envelope.Subscription);
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsForCustomerAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";

        using var response = await ReadAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await ReadJsonAsync<List<SubscriptionEnvelope>>(response, cancellationToken);

        return envelopes
            .Where(e => e.Subscription is not null)
            .Select(e => MapSubscription(e.Subscription!))
            .ToList();
    }

    private async Task<HttpResponseMessage> ReadAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        // Reads are idempotent, so transient transport failures / throttling can be retried
        // with a short backoff. Writes are never retried here - idempotency for them is
        // enforced deterministically in SubscriptionService.
        const int maxAttempts = 3;

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.GetAsync(pathAndQuery, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning("Maxio GET {Path} failed (attempt {Attempt}/{Max}): {Message}. Retrying.",
                    pathAndQuery, attempt, maxAttempts, ex.Message);
                await DelayBeforeRetryAsync(attempt, cancellationToken);
                continue;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < maxAttempts)
            {
                _logger.LogWarning("Maxio GET {Path} timed out (attempt {Attempt}/{Max}): {Message}. Retrying.",
                    pathAndQuery, attempt, maxAttempts, ex.Message);
                await DelayBeforeRetryAsync(attempt, cancellationToken);
                continue;
            }

            if (attempt < maxAttempts && IsTransientlyFailed(response))
            {
                response.Dispose();
                _logger.LogWarning("Maxio GET {Path} returned transient status (attempt {Attempt}/{Max}). Retrying.",
                    pathAndQuery, attempt, maxAttempts);
                await DelayBeforeRetryAsync(attempt, cancellationToken);
                continue;
            }

            return response;
        }
    }

    private static bool IsTransientlyFailed(HttpResponseMessage response) =>
        (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests;

    private static async Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var errors = MaxioErrorParser.Parse(await SafeReadBodyAsync(response, cancellationToken));
        var detail = errors.Count > 0 ? string.Join(" ", errors) : "no error details returned";
        var message = $"Maxio Advanced Billing API returned HTTP {(int)response.StatusCode} for {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}: {detail}";

        if (IsReferenceConflict(response.StatusCode, errors))
            throw new MaxioReferenceConflictException(message, errors);

        _logger.LogWarning("Maxio API error: {Message}", message);
        throw new MaxioApiException((int)response.StatusCode, message, errors);
    }

    private static bool IsReferenceConflict(HttpStatusCode statusCode, IReadOnlyList<string> errors)
    {
        if (statusCode != HttpStatusCode.UnprocessableEntity)
            return false;

        return errors.Any(e =>
        {
            var lower = e.ToLowerInvariant();
            return lower.Contains("reference") && (lower.Contains("taken") || lower.Contains("unique"));
        });
    }

    private async Task<string?> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not read Maxio error response body: {Message}", ex.Message);
            return null;
        }
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadFromJsonAsync<T>(Json.DeserializeOptions, cancellationToken);
        if (payload is null)
            throw new MaxioApiException((int)response.StatusCode, $"Maxio response could not be deserialized as {typeof(T).Name}.");

        return payload;
    }

    private static BillingCustomer MapCustomer(CustomerData customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference ?? string.Empty,
        Email = customer.Email ?? string.Empty,
        FirstName = customer.FirstName ?? string.Empty,
        LastName = customer.LastName ?? string.Empty,
        CreatedAt = customer.CreatedAt
    };

    private static BillingSubscription MapSubscription(SubscriptionData subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference ?? string.Empty,
        State = subscription.State ?? string.Empty,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0,
        NextBillingDate = subscription.NextAssessmentAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt,
        CanceledAt = subscription.CanceledAt,
        ExpiresAt = subscription.ExpiresAt
    };

    private static class Json
    {
        public static readonly System.Text.Json.JsonSerializerOptions DeserializeOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static readonly System.Text.Json.JsonSerializerOptions SerializeOptions = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = false
        };
    }
}
