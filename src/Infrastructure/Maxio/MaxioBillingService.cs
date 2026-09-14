using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="IBillingService"/> implemented against the Maxio Advanced Billing REST API.
/// Maxio is the system of record; this service holds no local subscription state.
/// </summary>
public class MaxioBillingService : IBillingService
{
    // States that mean the subscription is no longer a "live" enrollment. A subscription in any
    // other state is treated as an existing enrollment for idempotency purposes.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly MaxioSettings _settings;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        // Do not validate here: the service is constructed at startup (endpoints register routes),
        // so configuration is validated lazily on first use instead.
        _settings = settings.Value;
    }

    private string BaseUrl => _settings.ResolveBaseUrl();

    public async Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        _settings.Validate();

        // Products belonging to the configured product family are the subscribable plans.
        var url = $"{BaseUrl}/product_families/handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle!)}/products.json?per_page=200";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, "list subscription plans", cancellationToken);

        var products = await ReadJsonAsync<List<ProductListItem>>(response, cancellationToken) ?? new List<ProductListItem>();

        return products
            .Select(item => item.Product)
            .Where(p => p is { Handle: not null } && p.ArchivedAt is null)
            .Select(p => ToPlan(p!))
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        if (enrollment is null) throw new ArgumentNullException(nameof(enrollment));
        if (string.IsNullOrWhiteSpace(enrollment.UserReference)) throw new ArgumentException("UserReference is required.", nameof(enrollment));
        if (string.IsNullOrWhiteSpace(enrollment.PlanHandle)) throw new ArgumentException("PlanHandle is required.", nameof(enrollment));
        _settings.Validate();

        // 1. Ensure a single billing customer exists for this user (idempotent on reference).
        var customer = await EnsureCustomerAsync(enrollment, cancellationToken);

        // 2. If the user is already subscribed to this plan, return that subscription unchanged.
        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = FindLiveSubscriptionForPlan(subscriptions, enrollment.PlanHandle);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Customer {CustomerId} is already subscribed to plan {PlanHandle} (subscription {SubscriptionId}); returning existing.",
                customer.Id, enrollment.PlanHandle, existing.Id);
            return ToSubscription(existing, alreadyExisted: true);
        }

        // 3. Create the subscription. The uniqueness token makes a retried/duplicated POST safe.
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionAttributes
            {
                ProductHandle = enrollment.PlanHandle,
                CustomerId = customer.Id,
                // Bill by invoice so signup succeeds without capturing a card / 3-DS.
                PaymentCollectionMethod = "remittance",
                UniquenessToken = BuildUniquenessToken(enrollment.UserReference, enrollment.PlanHandle)
            }
        };

        using var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/subscriptions.json", request, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // A duplicate request (e.g. a double-click or a retried timeout) already created it.
            _logger.LogInformation(
                "Duplicate subscribe detected for customer {CustomerId} / plan {PlanHandle}; resolving existing subscription.",
                customer.Id, enrollment.PlanHandle);
            var afterConflict = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var resolved = FindLiveSubscriptionForPlan(afterConflict, enrollment.PlanHandle);
            if (resolved is not null)
            {
                return ToSubscription(resolved, alreadyExisted: true);
            }

            throw new BillingException(
                "A duplicate subscription request is in progress; please retry in a moment.",
                statusCode: (int)HttpStatusCode.Conflict);
        }

        await EnsureSuccessAsync(response, "create subscription", cancellationToken);

        var envelope = await ReadJsonAsync<SubscriptionEnvelope>(response, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new BillingException("The billing system returned an empty subscription response.");
        }

        _logger.LogInformation(
            "Created subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle} (state {State}).",
            envelope.Subscription.Id, customer.Id, enrollment.PlanHandle, envelope.Subscription.State);

        return ToSubscription(envelope.Subscription, alreadyExisted: false);
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userReference)) throw new ArgumentException("User reference is required.", nameof(userReference));
        _settings.Validate();

        var customer = await LookupCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(s => ToSubscription(s, alreadyExisted: true)).ToList();
    }

    // --- Customer helpers -------------------------------------------------------------------

    private async Task<CustomerWire> EnsureCustomerAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        var existing = await LookupCustomerByReferenceAsync(enrollment.UserReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerAttributes
            {
                FirstName = enrollment.FirstName,
                LastName = enrollment.LastName,
                Email = enrollment.Email,
                Reference = enrollment.UserReference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/customers.json", request, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Most likely a concurrent create won the race on the unique reference; re-read it.
            var afterRace = await LookupCustomerByReferenceAsync(enrollment.UserReference, cancellationToken);
            if (afterRace is not null)
            {
                return afterRace;
            }

            await EnsureSuccessAsync(response, "create billing customer", cancellationToken);
        }

        await EnsureSuccessAsync(response, "create billing customer", cancellationToken);

        var envelope = await ReadJsonAsync<CustomerEnvelope>(response, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new BillingException("The billing system returned an empty customer response.");
        }

        _logger.LogInformation("Created billing customer {CustomerId} for reference {Reference}.",
            envelope.Customer.Id, enrollment.UserReference);
        return envelope.Customer;
    }

    private async Task<CustomerWire?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up billing customer", cancellationToken);
        var envelope = await ReadJsonAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    private async Task<List<SubscriptionWire>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/customers/{customerId}/subscriptions.json";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);

        var items = await ReadJsonAsync<List<SubscriptionEnvelope>>(response, cancellationToken) ?? new List<SubscriptionEnvelope>();
        return items
            .Select(i => i.Subscription)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    private static SubscriptionWire? FindLiveSubscriptionForPlan(IEnumerable<SubscriptionWire> subscriptions, string planHandle)
        => subscriptions
            .Where(s => string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .Where(s => s.State is null || !TerminalStates.Contains(s.State))
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

    // --- Mapping ----------------------------------------------------------------------------

    private static SubscriptionPlan ToPlan(ProductWire product) => new(
        Handle: product.Handle!,
        Name: product.Name ?? product.Handle!,
        Description: product.Description,
        PriceInCents: (int)product.PriceInCents,
        Interval: product.Interval,
        IntervalUnit: product.IntervalUnit ?? "month");

    private static CustomerSubscription ToSubscription(SubscriptionWire s, bool alreadyExisted) => new(
        SubscriptionId: s.Id,
        State: s.State ?? "unknown",
        PlanHandle: s.Product?.Handle,
        PlanName: s.Product?.Name,
        PriceInCents: (int)(s.Product?.PriceInCents ?? 0),
        Interval: s.Product?.Interval ?? 0,
        IntervalUnit: s.Product?.IntervalUnit,
        CustomerId: s.Customer?.Id ?? 0,
        CustomerReference: s.Customer?.Reference,
        CurrentPeriodStartedAt: s.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt: s.CurrentPeriodEndsAt,
        // next_assessment_at is the next date a charge is attempted; fall back to period end.
        NextBillingAt: s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        CreatedAt: s.CreatedAt)
    {
        AlreadyExisted = alreadyExisted
    };

    // --- HTTP plumbing ----------------------------------------------------------------------

    private static string BuildUniquenessToken(string userReference, string planHandle)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"eshoponweb:subscribe:{userReference}:{planHandle}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new BillingException("Could not parse the response from the billing system.", ex,
                statusCode: (int)response.StatusCode);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ExtractErrors(body);
        _logger.LogWarning("Maxio request to {Operation} failed with {StatusCode}. Errors: {Errors}",
            operation, (int)response.StatusCode, errors.Count > 0 ? string.Join("; ", errors) : "(none)");

        throw new BillingException($"Failed to {operation} ({(int)response.StatusCode} {response.ReasonPhrase}).",
            statusCode: (int)response.StatusCode, errors: errors);
    }

    private static IReadOnlyCollection<string> ExtractErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        // "errors" may be an array of strings, a single string, or an object of field->message.
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return FlattenJsonMessages(errorsElement);
            }

            if (doc.RootElement.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String)
            {
                return new[] { errorElement.GetString()! };
            }
        }
        catch (JsonException)
        {
            // Non-JSON body; fall through to returning the raw text.
        }

        return new[] { body.Length > 500 ? body[..500] : body };
    }

    private static List<string> FlattenJsonMessages(JsonElement element)
    {
        var messages = new List<string>();
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                messages.Add(element.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    messages.AddRange(FlattenJsonMessages(item));
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var message in FlattenJsonMessages(property.Value))
                    {
                        messages.Add($"{property.Name}: {message}");
                    }
                }
                break;
            default:
                messages.Add(element.ToString());
                break;
        }

        return messages;
    }
}
