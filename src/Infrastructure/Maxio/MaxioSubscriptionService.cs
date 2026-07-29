using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="IMaxioSubscriptionService"/> backed by the Maxio Advanced Billing REST API.
/// Talks to Maxio exclusively over a pre-configured typed <see cref="HttpClient"/>
/// (base address + HTTP Basic auth are set up in <see cref="MaxioServiceCollectionExtensions"/>).
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Maxio subscription states that represent a "live" enrollment. When a subscriber is
    // already in one of these states for a plan, re-subscribing returns the existing
    // subscription instead of creating a duplicate.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "pending", "assessing", "past_due", "soft_failure", "paused"
    };

    // Serializes concurrent subscribe calls for the same subscriber so a double-click
    // cannot race two "customer does not exist yet" branches into two customers.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriberLocks = new();

    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        HttpClient http,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var uri = $"product_families/handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle)}/products.json?per_page=200";

        using var response = await _http.GetAsync(uri, cancellationToken);
        await EnsureSuccessAsync(response, "Failed to list subscription plans from Maxio", cancellationToken);

        var products = await response.Content.ReadFromJsonAsync<List<MaxioProductEnvelope>>(MaxioJson.Options, cancellationToken)
                       ?? new List<MaxioProductEnvelope>();

        return products
            .Where(p => p.Product is { ArchivedAt: null })
            .Select(p => MapPlan(p.Product!))
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioIntegrationException("A plan handle is required to subscribe.", (int)HttpStatusCode.BadRequest);
        }

        var gate = SubscriberLocks.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

            // Idempotency: if the subscriber already has a live subscription to this plan, return it.
            var existing = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var match = existing.FirstOrDefault(s => IsLivePlanMatch(s, planHandle));
            if (match is not null)
            {
                _logger.LogInformation($"Subscriber {subscriber.Reference} already has live subscription {match.Id} for plan '{planHandle}'; returning existing.");
                return MapSubscription(match, alreadyExisted: true);
            }

            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscriptionBody
                {
                    ProductHandle = planHandle,
                    CustomerId = customer.Id,
                    PaymentCollectionMethod = "remittance",
                    UniquenessToken = Guid.NewGuid().ToString("N")
                }
            };

            using var response = await _http.PostAsJsonAsync("subscriptions.json", request, MaxioJson.Options, cancellationToken);

            // 409 => Maxio already received an identical submission (duplicate-prevention);
            // resolve idempotently by returning the subscription that actually got created.
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var afterConflict = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
                var created = afterConflict.FirstOrDefault(s => IsLivePlanMatch(s, planHandle));
                if (created is not null)
                {
                    return MapSubscription(created, alreadyExisted: true);
                }
            }

            await EnsureSuccessAsync(response, $"Failed to create subscription for plan '{planHandle}'", cancellationToken);

            var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(MaxioJson.Options, cancellationToken);
            if (envelope?.Subscription is null)
            {
                throw new MaxioIntegrationException("Maxio returned an empty subscription response.");
            }

            _logger.LogInformation($"Created Maxio subscription {envelope.Subscription.Id} for subscriber {subscriber.Reference} on plan '{planHandle}'.");
            return MapSubscription(envelope.Subscription, alreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Select(s => MapSubscription(s, alreadyExisted: true))
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    // ----- Maxio calls -----

    private async Task<MaxioCustomer> EnsureCustomerAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerBody
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference
            }
        };

        using var response = await _http.PostAsJsonAsync("customers.json", request, MaxioJson.Options, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The only uniqueness constraint is on `reference`. A 422 here most likely means a
            // concurrent request created the customer first — re-resolve it instead of failing.
            var recovered = await FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            await EnsureSuccessAsync(response, "Failed to create Maxio customer", cancellationToken);
        }

        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(MaxioJson.Options, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioIntegrationException("Maxio returned an empty customer response.");
        }

        _logger.LogInformation($"Created Maxio customer {envelope.Customer.Id} for subscriber {subscriber.Reference}.");
        return envelope.Customer;
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var uri = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _http.GetAsync(uri, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "Failed to look up Maxio customer", cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(MaxioJson.Options, cancellationToken);
        return envelope?.Customer;
    }

    private async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var uri = $"customers/{customerId}/subscriptions.json";
        using var response = await _http.GetAsync(uri, cancellationToken);
        await EnsureSuccessAsync(response, "Failed to list customer subscriptions from Maxio", cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionEnvelope>>(MaxioJson.Options, cancellationToken)
                        ?? new List<MaxioSubscriptionEnvelope>();

        return envelopes.Where(e => e.Subscription is not null).Select(e => e.Subscription!).ToList();
    }

    // ----- mapping / helpers -----

    private static bool IsLivePlanMatch(MaxioSubscription subscription, string planHandle)
        => string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
           && subscription.State is not null
           && LiveStates.Contains(subscription.State);

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
        RequiresPaymentMethod = product.RequireCreditCard,
        ProductId = product.Id
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, bool alreadyExisted) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? "unknown",
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        ProductPriceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        AlreadyExisted = alreadyExisted
    };

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string context, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ParseErrors(body);
        _logger.LogWarning($"{context}: Maxio responded {(int)response.StatusCode}. {string.Join("; ", errors)}");
        throw new MaxioIntegrationException(context, (int)response.StatusCode, errors);
    }

    /// <summary>
    /// Extracts human-readable messages from a Maxio error body. Maxio returns errors as
    /// either <c>{"errors":["msg", ...]}</c>, <c>{"errors":{"field":"msg"}}</c>, or a bare
    /// string; this handles all shapes and falls back to the raw body.
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return ExtractMessages(errorsElement);
            }

            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                var value = document.RootElement.GetString();
                return value is null ? Array.Empty<string>() : new[] { value };
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through to raw body.
        }

        return new[] { body.Trim() };
    }

    private static IReadOnlyList<string> ExtractMessages(JsonElement errorsElement)
    {
        var messages = new List<string>();
        switch (errorsElement.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in errorsElement.EnumerateArray())
                {
                    var text = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        messages.Add(text!);
                    }
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in errorsElement.EnumerateObject())
                {
                    var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
                    messages.Add($"{property.Name}: {value}");
                }
                break;
            case JsonValueKind.String:
                var single = errorsElement.GetString();
                if (!string.IsNullOrWhiteSpace(single))
                {
                    messages.Add(single!);
                }
                break;
        }

        return messages;
    }
}
