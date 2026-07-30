using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// <see cref="IMaxioBillingService"/> implemented over the Maxio Advanced Billing REST API
/// using a pre-authenticated typed <see cref="HttpClient"/>. Every enrollment is keyed on the
/// customer's stable reference so retries and double-clicks are idempotent: an existing
/// customer is reused, an existing live subscription to the same plan is returned as-is, and
/// tight races are collapsed via Maxio's <c>uniqueness_token</c> duplicate prevention.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    // Maxio subscription states that mean "no longer a live enrollment"; anything else is
    // treated as an active enrollment for idempotency purposes.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // Serializes concurrent subscribe operations for the same customer within this instance,
    // so a double-click can't slip two creates past the "already subscribed?" check. Combined
    // with reconciling against Maxio (the source of truth) on every attempt, this keeps
    // enrollment idempotent across double-clicks and network-retry resubmissions. A multi-
    // instance deployment would additionally need a distributed lock.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CustomerLocks = new();

    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient http, IOptions<MaxioSettings> settings, ILogger<MaxioBillingService> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = RequireProductFamilyHandle();
        var url = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?per_page=200";

        using var response = await SendAsync(HttpMethod.Get, url, content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, "list subscription plans", cancellationToken);
        }

        var products = await ReadJsonAsync<List<ProductEnvelope>>(response, cancellationToken) ?? new List<ProductEnvelope>();
        return products
            .Select(p => p.Product)
            .Where(p => p is not null && p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p!.PriceInCents)
            .Select(p => ToPlan(p!))
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(BillingCustomer customer, string planHandle, CancellationToken cancellationToken = default)
    {
        if (customer is null) throw new ArgumentNullException(nameof(customer));
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException("A plan handle is required to subscribe.", statusCode: 400);
        }

        // Validate the plan against the configured family up front so an unknown handle is a
        // clean 404 rather than a confusing upstream validation error.
        var plans = await GetSubscriptionPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new BillingException(
                $"No subscription plan with handle '{planHandle}' exists in product family '{_settings.ProductFamilyHandle}'.",
                statusCode: 404);
        }

        var gate = CustomerLocks.GetOrAdd(customer.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var maxioCustomer = await EnsureCustomerAsync(customer, cancellationToken);

            // Idempotency: if this customer already has a live subscription to the plan, reuse
            // it. Because this reads Maxio (the source of truth), it also collapses a retry of a
            // create whose response was lost after the subscription was actually created.
            var existing = await ListCustomerSubscriptionsAsync(maxioCustomer.Id, cancellationToken);
            var live = existing
                .Where(s => MatchesPlan(s, planHandle) && !IsTerminal(s.State))
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefault();
            if (live is not null)
            {
                _logger.LogInformation("Reusing existing live subscription {SubscriptionId} for customer {Reference} on plan {Plan}.",
                    live.Id, customer.Reference, planHandle);
                return ToSubscription(live, alreadyExisted: true);
            }

            var request = new CreateSubscriptionRequest
            {
                Subscription = new MaxioSubscriptionInput
                {
                    ProductHandle = planHandle,
                    CustomerId = maxioCustomer.Id,
                    // Invoice-based collection so the subscription activates without a stored card
                    // (the eShop plans require no payment method at signup).
                    PaymentCollectionMethod = "remittance"
                }
            };

            using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", JsonContent.Create(request, options: JsonOptions), cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var envelope = await ReadJsonAsync<SubscriptionEnvelope>(response, cancellationToken);
                if (envelope?.Subscription is null)
                {
                    throw new BillingException("Maxio returned an empty response when creating the subscription.");
                }

                _logger.LogInformation("Created subscription {SubscriptionId} for customer {Reference} on plan {Plan}.",
                    envelope.Subscription.Id, customer.Reference, planHandle);
                return ToSubscription(envelope.Subscription, alreadyExisted: false);
            }

            // 422 means Maxio rejected the request itself (e.g. plan requires payment) — surface as a client error.
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                throw await CreateExceptionAsync(response, "create subscription", cancellationToken, clientError: true);
            }

            throw await CreateExceptionAsync(response, "create subscription", cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw new BillingException("A customer reference is required.", statusCode: 400);
        }

        var customer = await LookupCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => ToSubscription(s, alreadyExisted: false))
            .ToList();
    }

    // ----- Maxio calls -----------------------------------------------------------------

    private async Task<MaxioCustomer> EnsureCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken)
    {
        var existing = await LookupCustomerAsync(customer.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new CreateCustomerRequest
        {
            Customer = new MaxioCustomerInput
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        using var response = await SendAsync(HttpMethod.Post, "customers.json", JsonContent.Create(request, options: JsonOptions), cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var envelope = await ReadJsonAsync<CustomerEnvelope>(response, cancellationToken);
            if (envelope?.Customer is not null)
            {
                _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.",
                    envelope.Customer.Id, customer.Reference);
                return envelope.Customer;
            }
        }

        // A concurrent create won the race: the reference is now taken (422) or the token
        // collided (409). Either way the customer exists now — look it up again.
        if (response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
        {
            var recovered = await LookupCustomerAsync(customer.Reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }
        }

        throw await CreateExceptionAsync(response, "create the billing customer", cancellationToken);
    }

    private async Task<MaxioCustomer?> LookupCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(HttpMethod.Get, url, content: null, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, "look up the billing customer", cancellationToken);
        }

        var envelope = await ReadJsonAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    private async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var url = $"customers/{customerId}/subscriptions.json";
        using var response = await SendAsync(HttpMethod.Get, url, content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, "list the customer's subscriptions", cancellationToken);
        }

        var envelopes = await ReadJsonAsync<List<SubscriptionEnvelope>>(response, cancellationToken) ?? new List<SubscriptionEnvelope>();
        return envelopes.Select(e => e.Subscription).Where(s => s is not null).Select(s => s!).ToList();
    }

    // ----- HTTP plumbing ---------------------------------------------------------------

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(method, url) { Content = content };
        try
        {
            return await _http.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingException($"Could not reach Maxio: {ex.Message}", statusCode: 502);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingException($"The Maxio request timed out: {ex.Message}", statusCode: 504);
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new BillingException($"Maxio returned a response that could not be parsed: {ex.Message}");
        }
    }

    private async Task<BillingException> CreateExceptionAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken, bool clientError = false)
    {
        string body = string.Empty;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            // best effort — fall through with an empty body
        }

        var messages = TryExtractErrors(body);
        var detail = messages.Count > 0 ? string.Join("; ", messages) : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";

        var statusCode = clientError
            ? 400
            : response.StatusCode == HttpStatusCode.TooManyRequests ? 503 : 502;

        _logger.LogError("Maxio request to {Action} failed with {Status}: {Detail}", action, (int)response.StatusCode, detail);
        return new BillingException($"Maxio request failed while trying to {action}: {detail}", statusCode, messages);
    }

    private static IReadOnlyList<string> TryExtractErrors(string body)
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

            var results = new List<string>();
            switch (errors.ValueKind)
            {
                case JsonValueKind.Array:
                    results.AddRange(errors.EnumerateArray().Select(e => e.ToString()));
                    break;
                case JsonValueKind.Object:
                    foreach (var prop in errors.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            results.AddRange(prop.Value.EnumerateArray().Select(e => $"{prop.Name}: {e}"));
                        }
                        else
                        {
                            results.Add($"{prop.Name}: {prop.Value}");
                        }
                    }
                    break;
                case JsonValueKind.String:
                    results.Add(errors.GetString() ?? string.Empty);
                    break;
            }

            return results.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    // ----- Mapping ---------------------------------------------------------------------

    private string RequireProductFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new BillingException("Maxio is not configured: 'Maxio:ProductFamilyHandle' is missing.", statusCode: 500);
        }
        return _settings.ProductFamilyHandle.Trim();
    }

    private static bool IsTerminal(string? state) => state is not null && TerminalStates.Contains(state);

    private static bool MatchesPlan(MaxioSubscription subscription, string planHandle) =>
        string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase);

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        FormattedPrice = FormatMoney(product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty
    };

    private static CustomerSubscription ToSubscription(MaxioSubscription subscription, bool alreadyExisted) => new()
    {
        Id = subscription.Id,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference,
        State = subscription.State ?? "unknown",
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        ProductFamilyHandle = subscription.Product?.ProductFamily?.Handle,
        PriceInCents = subscription.ProductPriceInCents,
        FormattedPrice = subscription.ProductPriceInCents is { } cents ? FormatMoney(cents) : null,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt,
        AlreadyExisted = alreadyExisted
    };

    private static string FormatMoney(long cents) =>
        "$" + (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
