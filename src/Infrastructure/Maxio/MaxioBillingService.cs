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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Talks to the Maxio Advanced Billing REST API over a typed <see cref="HttpClient"/>.
/// Maxio is the system of record: customers are keyed by a stable <c>reference</c>
/// (the eShopOnWeb username) so the integration is idempotent across process restarts
/// even though eShopOnWeb itself may run against an in-memory database.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // Serializes concurrent subscribe attempts for the same customer within this process,
    // so a double-click cannot race past the "already-subscribed" pre-check.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var familyHandle = _settings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new MaxioConfigurationException(
                "No product family configured. Set 'Maxio:ProductFamilyHandle' to expose subscription plans.");
        }

        // handle: prefix lets us address the family by its stable handle rather than a numeric id.
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?per_page=200";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, "list subscription plans", cancellationToken);

        var products = await DeserializeAsync<List<MaxioProductEnvelope>>(response, cancellationToken)
                       ?? new List<MaxioProductEnvelope>();

        return products
            .Select(p => p.Product)
            .Where(p => p is not null && p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => MapPlan(p!))
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(command.PlanHandle))
        {
            throw new MaxioBillingException("A plan handle is required to subscribe.", (int)HttpStatusCode.BadRequest);
        }

        // A single uniqueness token covers this whole attempt so Maxio can de-duplicate
        // any request that is transparently retried by the resilience handler.
        var uniquenessToken = Guid.NewGuid().ToString();

        var gate = SubscribeLocks.GetOrAdd(command.CustomerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var (customer, customerCreated) = await EnsureCustomerAsync(command, uniquenessToken, cancellationToken);

            var existing = await FindLiveSubscriptionAsync(customer.Id, command.PlanHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Customer {CustomerId} already has a live subscription {SubscriptionId} to plan {PlanHandle}; returning it.",
                    customer.Id, existing.Id, command.PlanHandle);
                return new SubscribeResult(existing, AlreadyExisted: true, customer.Id, customerCreated);
            }

            var created = await CreateSubscriptionAsync(customer.Id, command.PlanHandle, uniquenessToken, cancellationToken);
            _logger.LogInformation(
                "Created subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle} ({State}).",
                created.Id, customer.Id, command.PlanHandle, created.State);
            return new SubscribeResult(created, AlreadyExisted: false, customer.Id, customerCreated);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var customer = await LookupCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    // ----- customer -----

    private async Task<(MaxioCustomer Customer, bool Created)> EnsureCustomerAsync(
        SubscribeCommand command, string uniquenessToken, CancellationToken cancellationToken)
    {
        var existing = await LookupCustomerAsync(command.CustomerReference, cancellationToken);
        if (existing is not null)
        {
            return (existing, false);
        }

        var body = new
        {
            customer = new
            {
                first_name = command.FirstName,
                last_name = command.LastName,
                email = command.Email,
                reference = command.CustomerReference
            },
            uniqueness_token = uniquenessToken
        };

        using var response = await _httpClient.PostAsync(
            "customers.json", JsonContent.Create(body, options: JsonOptions), cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var created = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
            if (created?.Customer is null)
            {
                throw new MaxioBillingException("Maxio returned an empty customer on create.", (int)response.StatusCode);
            }
            return (created.Customer, true);
        }

        // Another concurrent/duplicate request may have created the customer first. Maxio
        // enforces reference uniqueness (422) and duplicate-prevention (409); in both cases
        // the customer now exists, so re-read it and continue idempotently.
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            var reloaded = await LookupCustomerAsync(command.CustomerReference, cancellationToken);
            if (reloaded is not null)
            {
                return (reloaded, false);
            }
        }

        throw await BuildExceptionAsync(response, "create customer", cancellationToken);
    }

    private async Task<MaxioCustomer?> LookupCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up customer", cancellationToken);
        var envelope = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    // ----- subscriptions -----

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions
            .Where(s => s.IsLive && string.Equals(s.ProductHandle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
    }

    private async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json?per_page=200";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);

        var items = await DeserializeAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken)
                    ?? new List<MaxioSubscriptionEnvelope>();

        return items
            .Select(i => i.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    private async Task<CustomerSubscription> CreateSubscriptionAsync(int customerId, string planHandle, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = planHandle,
                customer_id = customerId,
                // These demo plans do not require a payment method; remittance (invoice)
                // billing lets the subscription activate without capturing a card / 3-DS.
                payment_collection_method = "remittance"
            },
            uniqueness_token = uniquenessToken
        };

        using var response = await _httpClient.PostAsync(
            "subscriptions.json", JsonContent.Create(body, options: JsonOptions), cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var envelope = await DeserializeAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
            if (envelope?.Subscription is null)
            {
                throw new MaxioBillingException("Maxio returned an empty subscription on create.", (int)response.StatusCode);
            }
            return MapSubscription(envelope.Subscription);
        }

        // Duplicate-prevention: a retried POST that Maxio already processed returns 409.
        // The subscription exists, so re-read and return it rather than failing.
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var found = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
            if (found is not null)
            {
                return found;
            }
        }

        throw await BuildExceptionAsync(response, "create subscription", cancellationToken);
    }

    // ----- mapping -----

    private static SubscriptionPlan MapPlan(MaxioProduct p) => new(
        Handle: p.Handle!,
        Name: p.Name ?? p.Handle!,
        Description: p.Description,
        PriceInCents: (int)p.PriceInCents,
        Price: p.PriceInCents / 100m,
        Interval: p.Interval,
        IntervalUnit: p.IntervalUnit ?? "month",
        ProductId: p.Id);

    private static CustomerSubscription MapSubscription(MaxioSubscription s)
    {
        var priceInCents = s.Product?.PriceInCents ?? s.ProductPriceInCents;
        return new CustomerSubscription(
            Id: s.Id,
            State: s.State ?? "unknown",
            ProductHandle: s.Product?.Handle,
            ProductName: s.Product?.Name,
            PriceInCents: (int)priceInCents,
            Price: priceInCents / 100m,
            Interval: s.Product?.Interval ?? 0,
            IntervalUnit: s.Product?.IntervalUnit,
            CurrentPeriodEndsAt: s.CurrentPeriodEndsAt,
            NextAssessmentAt: s.NextAssessmentAt,
            ActivatedAt: s.ActivatedAt,
            CreatedAt: s.CreatedAt,
            CustomerReference: s.Customer?.Reference,
            CustomerId: s.Customer?.Id ?? 0);
    }

    // ----- infrastructure -----

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured || _httpClient.BaseAddress is null)
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Provide 'Maxio:ApiKey' and either 'Maxio:Subdomain' or 'Maxio:BaseUrl' " +
                "(via user-secrets or environment variables).");
        }
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildExceptionAsync(response, operation, cancellationToken);
        }
    }

    private async Task<MaxioBillingException> BuildExceptionAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = TryParseErrors(body);
        _logger.LogWarning(
            "Maxio call to {Operation} failed with {StatusCode}: {Body}",
            operation, (int)response.StatusCode, body);
        return new MaxioBillingException(
            $"Maxio failed to {operation} (HTTP {(int)response.StatusCode}).",
            (int)response.StatusCode,
            errors);
    }

    private static IReadOnlyList<string> TryParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
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
}
