using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> implementation backed by the Maxio Advanced Billing
/// (Chargify) REST API. The API base address and HTTP Basic authorization are configured on the
/// injected <see cref="HttpClient"/> (see the registration extension); this class owns the request
/// shapes, idempotency, and mapping to provider-neutral models.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // States in which a subscription is considered still "live", so a repeat subscribe should reuse it
    // rather than create a duplicate. Anything not in this terminal set is treated as live.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    // Serializes the check-then-create sequence per subscriber within this process, so concurrent
    // subscribe requests (e.g. a double-click) can't both observe "no subscription yet" and each
    // create one. Maxio has no native idempotency key for subscription creation, and this host runs
    // as a single instance, so an in-process keyed lock is the appropriate guard.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        using var response = await _httpClient.GetAsync($"product_families/{familyId}/products.json", cancellationToken);
        await EnsureSuccessAsync(response, "list subscription plans", cancellationToken);

        var products = await DeserializeAsync<List<MaxioProductEnvelope>>(response, cancellationToken) ?? new();

        return products
            .Select(p => p.Product)
            .Where(p => p is { ArchivedAt: null })
            .Select(MapPlan!)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(SubscriberInfo subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new PlanNotFoundException(planHandle ?? string.Empty);
        }

        // Only allow subscribing to a plan that belongs to the configured product family.
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new PlanNotFoundException(planHandle);
        }

        // Serialize the ensure-customer + dedup + create sequence for this subscriber so concurrent
        // requests cannot each create a customer or a subscription.
        var gate = SubscribeGates.GetOrAdd(BuildCustomerReference(subscriber.UserName), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // 1) Ensure exactly one billing customer exists for this user (idempotent, race-safe).
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

            // 2) Reuse a live subscription to the same plan instead of creating a duplicate.
            var existing = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var live = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) && IsLive(s.State));
            if (live is not null)
            {
                _logger.LogInformation("Reusing existing {0} subscription {1} for customer {2}.", live.State ?? "", live.Id, customer.Id);
                return MapSubscription(live, customer);
            }

            // 3) Enroll. "remittance" collection means no payment method / card is required at signup;
            //    Maxio issues an invoice for the balance instead of attempting an immediate card charge.
            var payload = new CreateSubscriptionEnvelope
            {
                Subscription = new SubscriptionAttributes
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    PaymentCollectionMethod = "remittance"
                }
            };

            using var response = await PostAsync("subscriptions.json", payload, cancellationToken);
            await EnsureSuccessAsync(response, "create subscription", cancellationToken);

            var created = await DeserializeAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
            if (created?.Subscription is null)
            {
                throw new SubscriptionBillingException("Maxio returned an empty subscription when creating the subscription.");
            }

            _logger.LogInformation("Created subscription {0} to plan '{1}' for customer {2}.", created.Subscription.Id, plan.Handle, customer.Id);
            return MapSubscription(created.Subscription, customer);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberInfo subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var customer = await LookupCustomerAsync(BuildCustomerReference(subscriber.UserName), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(s => MapSubscription(s, customer)).ToList();
    }

    // --- Maxio operations -------------------------------------------------------------------

    private async Task<long> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("product_families.json", cancellationToken);
        await EnsureSuccessAsync(response, "list product families", cancellationToken);

        var families = await DeserializeAsync<List<MaxioProductFamilyEnvelope>>(response, cancellationToken) ?? new();
        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null &&
                string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            throw new SubscriptionBillingException(
                $"The configured Maxio product family '{_settings.ProductFamilyHandle}' was not found on this site.");
        }

        return match.Id;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberInfo subscriber, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(subscriber.UserName);

        var existing = await LookupCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var payload = new CreateCustomerEnvelope
        {
            Customer = new CustomerAttributes
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = reference
            }
        };

        using var response = await PostAsync("customers.json", payload, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var created = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
            if (created?.Customer is null)
            {
                throw new SubscriptionBillingException("Maxio returned an empty customer when creating the customer.");
            }

            _logger.LogInformation("Created Maxio customer {0} for reference '{1}'.", created.Customer.Id, reference);
            return created.Customer;
        }

        // A concurrent request (double-click) may have created the customer first: Maxio enforces a
        // uniqueness constraint on 'reference' and returns 422. Re-read and reuse the winner.
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await LookupCustomerAsync(reference, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation("Reusing Maxio customer {0} created by a concurrent request (reference '{1}').", raced.Id, reference);
                return raced;
            }
        }

        await ThrowBillingErrorAsync(response, "create customer", cancellationToken);
        throw new SubscriptionBillingException("Unreachable."); // satisfies the compiler; ThrowBillingErrorAsync always throws.
    }

    private async Task<MaxioCustomer?> LookupCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up customer", cancellationToken);
        var envelope = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    private async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);

        var envelopes = await DeserializeAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken) ?? new();
        return envelopes.Select(e => e.Subscription).Where(s => s is not null).Select(s => s!).ToList();
    }

    // --- Mapping ----------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        ProductId = product.Id,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, MaxioCustomer customer) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? subscription.CurrentBillingAmountInCents ?? 0,
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        NextAssessmentDate = subscription.NextAssessmentAt,
        CustomerId = subscription.Customer?.Id ?? customer.Id,
        CustomerReference = subscription.Customer?.Reference ?? customer.Reference ?? string.Empty,
        CreatedAt = subscription.CreatedAt
    };

    private static bool IsLive(string? state) => !string.IsNullOrWhiteSpace(state) && !TerminalStates.Contains(state);

    /// <summary>
    /// Derives a stable, unique external reference from the eShopOnWeb user name. This is the
    /// idempotency anchor: a given user always maps to the same Maxio customer, even across restarts.
    /// </summary>
    private static string BuildCustomerReference(string userName) => $"eshop-user-{userName}";

    // --- HTTP plumbing ----------------------------------------------------------------------

    private Task<HttpResponseMessage> PostAsync<T>(string relativeUrl, T body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return _httpClient.PostAsync(relativeUrl, content, cancellationToken);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        await ThrowBillingErrorAsync(response, action, cancellationToken);
    }

    private async Task ThrowBillingErrorAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        var body = await SafeReadBodyAsync(response, cancellationToken);
        var detail = ExtractErrorMessage(body);

        var message = $"Maxio request to {action} failed with status {(int)response.StatusCode} ({response.StatusCode}).";
        if (!string.IsNullOrWhiteSpace(detail))
        {
            message += $" {detail}";
        }

        _logger.LogWarning("{0} Response body: {1}", message, Truncate(body));
        throw new SubscriptionBillingException(message);
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var array = JsonSerializer.Deserialize<MaxioErrorArrayResponse>(body, JsonOptions);
            if (array?.Errors is { Count: > 0 })
            {
                return string.Join("; ", array.Errors);
            }
        }
        catch (JsonException)
        {
            // errors may be a map rather than an array; fall through.
        }

        try
        {
            var map = JsonSerializer.Deserialize<MaxioErrorMapResponse>(body, JsonOptions);
            if (map?.Errors is { Count: > 0 })
            {
                return string.Join("; ", map.Errors.Select(kvp => $"{kvp.Key}: {string.Join(", ", kvp.Value)}"));
            }
        }
        catch (JsonException)
        {
            // not a recognizable error envelope.
        }

        return null;
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "...";
}
