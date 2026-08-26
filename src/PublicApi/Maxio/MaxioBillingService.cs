using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Implements the subscription billing flows against the Maxio Advanced Billing REST API.
/// Authentication is HTTP Basic with the API key as username and "X" as password.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    // States in which a subscription no longer bills. A subscription in one of these
    // does not block subscribing to the same plan again.
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended", "suspended"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new MaxioNullableDateTimeOffsetConverter() }
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListSubscriptionPlansAsync(CancellationToken cancellationToken = default)
    {
        // The product family is addressed by its stable handle; numeric ids change when a site is re-seeded.
        var familyReference = Uri.EscapeDataString($"handle:{_settings.ProductFamilyHandle}");
        var plans = new List<SubscriptionPlanDto>();

        const int perPage = 200;
        for (var page = 1; ; page++)
        {
            var url = $"product_families/{familyReference}/products.json?page={page}&per_page={perPage}";
            var batch = await GetAsync<List<MaxioProductEnvelope>>(url, cancellationToken) ?? new List<MaxioProductEnvelope>();
            if (batch.Count == 0)
            {
                break;
            }

            plans.AddRange(batch
                .Select(envelope => envelope.Product)
                .Where(product => product.ArchivedAt is null)
                .Select(MapToPlanDto));

            if (batch.Count < perPage)
            {
                break;
            }
        }

        return plans;
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerByReferenceAsync(userName, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await GetAsync<List<MaxioSubscriptionEnvelope>>($"customers/{customer.Id}/subscriptions.json", cancellationToken)
            ?? new List<MaxioSubscriptionEnvelope>();

        return subscriptions.Select(envelope => MapToDto(envelope.Subscription)).ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default)
    {
        var plans = await ListSubscriptionPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        await EnsureCustomerAsync(userName, cancellationToken);

        // A deterministic reference per shopper+plan makes subscribe idempotent:
        // retries and double-clicks find the existing subscription instead of creating another.
        var baseReference = BuildSubscriptionReference(userName, plan.Handle);
        var existing = await FindSubscriptionByReferenceAsync(baseReference, cancellationToken);
        if (existing is not null && !IsEndOfLife(existing.State))
        {
            return new SubscribeResult(MapToDto(existing), Created: false);
        }

        // Re-subscribing after cancellation reuses neither the dead subscription nor its
        // reference (the lookup above must keep returning the live one), so suffix a fresh id.
        var reference = existing is null ? baseReference : $"{baseReference}:{Guid.NewGuid():N}";

        try
        {
            var subscription = await CreateSubscriptionAsync(plan.Handle, userName, reference, reference, cancellationToken);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for {UserName} on plan {PlanHandle}",
                subscription.Id, userName, plan.Handle);
            return new SubscribeResult(MapToDto(subscription), Created: true);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // The uniqueness token was seen within the last 60 minutes, meaning an earlier
            // request with this token already completed (double-click or retry).
            var duplicate = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (duplicate is not null)
            {
                return new SubscribeResult(MapToDto(duplicate), Created: false);
            }

            // No subscription exists, so the earlier request failed before creating anything
            // (e.g. a validation error) but still consumed the token. Safe to retry once with
            // a fresh token; the deterministic subscription reference is unchanged.
            var retried = await CreateSubscriptionAsync(plan.Handle, userName, reference, $"{reference}:{Guid.NewGuid():N}", cancellationToken);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for {UserName} on plan {PlanHandle} after uniqueness-token retry",
                retried.Id, userName, plan.Handle);
            return new SubscribeResult(MapToDto(retried), Created: true);
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userName, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(userName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(userName);
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = userName,
                Reference = userName
            }
        };

        try
        {
            var response = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope>("customers.json", request, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for {UserName}", response!.Customer.Id, userName);
            return response.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Customer references are unique in Maxio; a 422 here means a concurrent request
            // won the create race. Return the winner.
            var winner = await FindCustomerByReferenceAsync(userName, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription> CreateSubscriptionAsync(string planHandle, string customerReference, string subscriptionReference, string uniquenessToken, CancellationToken cancellationToken)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionAttributes
            {
                ProductHandle = planHandle,
                CustomerReference = customerReference,
                Reference = subscriptionReference,
                // eShopOnWeb never collects payment instruments, so enroll on remittance
                // (invoice) terms: no charge is attempted at signup and none is required.
                PaymentCollectionMethod = "remittance"
            },
            UniquenessToken = uniquenessToken
        };

        var response = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope>("subscriptions.json", request, cancellationToken);
        return response!.Subscription;
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var envelope = await TryGetAsync<MaxioCustomerEnvelope>($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        return envelope?.Customer;
    }

    private async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var envelope = await TryGetAsync<MaxioSubscriptionEnvelope>($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        return envelope?.Subscription;
    }

    private static bool IsEndOfLife(string state) => EndOfLifeStates.Contains(state);

    private static string BuildSubscriptionReference(string userName, string planHandle) =>
        $"eshoponweb:{userName}:{planHandle}";

    // eShopOnWeb identities carry only an email address, so a display name is synthesized
    // from the local part to satisfy Maxio's required first/last name fields.
    private static (string FirstName, string LastName) DeriveName(string userName)
    {
        var localPart = userName.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        return parts.Length >= 2
            ? (textInfo.ToTitleCase(parts[0]), textInfo.ToTitleCase(parts[^1]))
            : (textInfo.ToTitleCase(localPart), "Customer");
    }

    private static SubscriptionPlanDto MapToPlanDto(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Description = product.Description,
        Price = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        SetupFee = product.InitialChargeInCents / 100m,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static SubscriptionDto MapToDto(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        State = subscription.State,
        Price = subscription.ProductPriceInCents / 100m,
        NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt
    };

    private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken) where T : class
    {
        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private async Task<T?> TryGetAsync<T>(string relativeUrl, CancellationToken cancellationToken) where T : class
    {
        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string relativeUrl, TRequest body, CancellationToken cancellationToken) where TResponse : class
    {
        using var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(relativeUrl, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeAsync<TResponse>(response, cancellationToken);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, ExtractErrorMessage(body));
    }

    // Maxio error bodies are usually {"errors": ["..."]} but some endpoints return an object.
    private static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "no response body";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    return string.Join("; ", errors.EnumerateArray().Select(e => e.ToString()));
                }

                if (errors.ValueKind == JsonValueKind.Object)
                {
                    return string.Join("; ", errors.EnumerateObject().Select(p => $"{p.Name}: {p.Value}"));
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall through to the raw body.
        }

        return body.Length <= 500 ? body : body.Substring(0, 500);
    }
}
