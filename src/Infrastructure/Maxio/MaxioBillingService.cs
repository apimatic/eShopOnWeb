using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by the Maxio Advanced Billing
/// JSON API. Maxio is the system of record: customers and subscriptions live in
/// Maxio and are looked up by the eShopOnWeb user's stable reference, so no local
/// billing state needs to be persisted.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    // Subscriptions in one of these states are considered dead — a new subscribe
    // should proceed rather than treat them as an existing enrollment.
    private static readonly HashSet<string> DeadStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    // Serializes concurrent subscribe attempts for the same user+plan within this
    // process, so a double-click cannot slip two creates past the "already
    // subscribed?" check. Maxio is authoritative across processes; this guards the
    // check-then-create window inside a single instance.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient http, IOptions<MaxioSettings> settings, ILogger<MaxioBillingService> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;

        // Fail fast with a clear message the first time the billing service is used
        // without configuration, rather than emitting a confusing request to an
        // unconfigured host.
        _settings.Validate();
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle)}/products.json?per_page=200";
        var envelopes = await GetJsonAsync<List<MaxioProductEnvelope>>(path, cancellationToken).ConfigureAwait(false);

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && !string.IsNullOrEmpty(p.Handle) && string.IsNullOrEmpty(p.ArchivedAt))
            .Select(MapPlan!)
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (string.IsNullOrWhiteSpace(command.UserReference)) throw new ArgumentException("User reference is required.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.PlanHandle)) throw new ArgumentException("Plan handle is required.", nameof(command));

        // Validate the plan up-front so an unknown handle yields a clean 404 rather
        // than a confusing downstream 422.
        var plans = await GetPlansAsync(cancellationToken).ConfigureAwait(false);
        if (!plans.Any(p => string.Equals(p.Handle, command.PlanHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new MaxioBillingException(
                $"Unknown or unavailable subscription plan '{command.PlanHandle}'.", (int)HttpStatusCode.NotFound);
        }

        var lockKey = $"{command.UserReference}|{command.PlanHandle}";
        var gate = SubscribeLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var customer = await EnsureCustomerAsync(command, cancellationToken).ConfigureAwait(false);

            // Idempotency: if the user already has a live subscription to this plan,
            // return it instead of creating a duplicate.
            var existing = await GetSubscriptionsForCustomerAsync(customer.Id, cancellationToken).ConfigureAwait(false);
            var live = existing.FirstOrDefault(s =>
                string.Equals(s.PlanHandle, command.PlanHandle, StringComparison.OrdinalIgnoreCase)
                && !DeadStates.Contains(s.State));

            if (live is not null)
            {
                _logger.LogInformation(
                    "User {Reference} already has live subscription {SubscriptionId} to plan {Plan}; returning existing.",
                    command.UserReference, live.Id, command.PlanHandle);
                return new SubscribeResult(live, alreadySubscribed: true);
            }

            var created = await CreateSubscriptionAsync(customer.Id, command.PlanHandle, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Created subscription {SubscriptionId} for user {Reference} on plan {Plan} (state {State}).",
                created.Id, command.UserReference, command.PlanHandle, created.State);
            return new SubscribeResult(created, alreadySubscribed: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userReference)) throw new ArgumentException("User reference is required.", nameof(userReference));

        var customer = await LookupCustomerAsync(userReference, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await GetSubscriptionsForCustomerAsync(customer.Id, cancellationToken).ConfigureAwait(false);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    // ---- Customer helpers -------------------------------------------------

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        var existing = await LookupCustomerAsync(command.UserReference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var body = new
        {
            customer = new
            {
                first_name = command.FirstName,
                last_name = command.LastName,
                email = command.Email,
                reference = command.UserReference
            }
        };

        using var response = await PostAsync("customers.json", body, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken).ConfigureAwait(false);
            if (envelope.Customer is not null)
            {
                _logger.LogInformation("Created Maxio customer {CustomerId} for user {Reference}.",
                    envelope.Customer.Id, command.UserReference);
                return envelope.Customer;
            }
        }

        // A concurrent create (or a create in a previous, since-lost run) can make
        // the reference already taken. Recover by re-reading the customer.
        if (response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
        {
            var recovered = await LookupCustomerAsync(command.UserReference, cancellationToken).ConfigureAwait(false);
            if (recovered is not null)
            {
                return recovered;
            }
        }

        await ThrowFromResponseAsync(response, "Failed to create Maxio customer", cancellationToken).ConfigureAwait(false);
        throw new MaxioBillingException("Failed to create Maxio customer."); // unreachable
    }

    private async Task<MaxioCustomer?> LookupCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowFromResponseAsync(response, "Failed to look up Maxio customer", cancellationToken).ConfigureAwait(false);
        }

        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken).ConfigureAwait(false);
        return envelope.Customer;
    }

    // ---- Subscription helpers ---------------------------------------------

    private async Task<CustomerSubscription> CreateSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = planHandle,
                customer_id = customerId,
                // The seeded plans do not require a stored payment method; remittance
                // (invoice) collection lets us enroll without capturing a card.
                payment_collection_method = "remittance",
                // Defense-in-depth against duplicate submission on transport retries.
                uniqueness_token = Guid.NewGuid().ToString("N")
            }
        };

        using var response = await PostAsync("subscriptions.json", body, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            var envelope = await ReadJsonAsync<MaxioSubscriptionEnvelope>(response, cancellationToken).ConfigureAwait(false);
            if (envelope.Subscription is not null)
            {
                return MapSubscription(envelope.Subscription);
            }
        }

        // Duplicate prevention: recover by returning the live subscription created
        // by the winning request.
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var existing = await GetSubscriptionsForCustomerAsync(customerId, cancellationToken).ConfigureAwait(false);
            var live = existing.FirstOrDefault(s =>
                string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase)
                && !DeadStates.Contains(s.State));
            if (live is not null)
            {
                return live;
            }
        }

        await ThrowFromResponseAsync(response, "Failed to create subscription", cancellationToken).ConfigureAwait(false);
        throw new MaxioBillingException("Failed to create subscription."); // unreachable
    }

    private async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForCustomerAsync(long customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await GetJsonAsync<List<MaxioSubscriptionEnvelope>>(path, cancellationToken).ConfigureAwait(false);
        return envelopes
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(MapSubscription!)
            .ToList();
    }

    // ---- Mapping ----------------------------------------------------------

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = "USD",
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        PricePointHandle = product.ProductPricePointHandle,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency ?? "USD",
        NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CreatedAt = subscription.CreatedAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod
    };

    // ---- HTTP plumbing ----------------------------------------------------

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowFromResponseAsync(response, $"Maxio GET {path} failed", cancellationToken).ConfigureAwait(false);
        }

        return await ReadJsonAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> PostAsync(string path, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _http.PostAsync(path, content, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (value is null)
        {
            throw new MaxioBillingException("Received an empty or unparseable response from Maxio.",
                (int)response.StatusCode);
        }

        return value;
    }

    private async Task ThrowFromResponseAsync(HttpResponseMessage response, string message, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var errors = ParseErrors(body);
        _logger.LogWarning("{Message}. Status {StatusCode}. Errors: {Errors}",
            message, (int)response.StatusCode, errors.Count > 0 ? string.Join("; ", errors) : "(none)");
        throw new MaxioBillingException(message, (int)response.StatusCode, errors);
    }

    private static IReadOnlyList<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            switch (errorsElement.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in errorsElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            result.Add(item.GetString()!);
                        }
                    }
                    break;

                case JsonValueKind.Object:
                    // { "field": ["msg", ...], ... }
                    foreach (var property in errorsElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in property.Value.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                {
                                    result.Add($"{property.Name}: {item.GetString()}");
                                }
                            }
                        }
                        else if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            result.Add($"{property.Name}: {property.Value.GetString()}");
                        }
                    }
                    break;

                case JsonValueKind.String:
                    result.Add(errorsElement.GetString()!);
                    break;
            }

            return result;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
