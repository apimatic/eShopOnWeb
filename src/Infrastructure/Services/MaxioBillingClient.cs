using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The one and only place eShopOnWeb talks to Maxio Advanced Billing. Everything else in the
/// application depends on <see cref="IBillingClient"/> instead, so retargeting the provider — or
/// pointing this build at production, a sandbox tenant, or a local mock — never leaks past this
/// class.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    /// <summary>Maxio authenticates with HTTP Basic: the API key as username, a literal "x" as password.</summary>
    private const string ApiKeyPasswordPlaceholder = "x";

    /// <summary>
    /// Bills the subscription by invoice instead of charging a stored card. Required for plans
    /// that carry a price but capture no payment method.
    /// </summary>
    private const string RemittancePaymentCollection = "remittance";

    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingClient> _logger;
    private int? _productFamilyId;

    public MaxioBillingClient(HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        // The composition root normally sets this from MaxioSettings.ResolveBaseUrl(); resolving
        // it here too keeps the client correct when constructed directly (for example in tests).
        _httpClient.BaseAddress ??= _settings.ResolveBaseUrl();

        _httpClient.DefaultRequestHeaders.Authorization ??= BuildAuthenticationHeader(_settings.ApiKey);

        if (!_httpClient.DefaultRequestHeaders.Accept.Any())
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    public string MeteredComponentHandle => _settings.MeteredComponentHandle;

    public async Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(
        CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{EscapeHandle(_settings.ProductFamilyHandle)}/products.json";

        var products = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null,
            "list plans", cancellationToken);

        if (products is null)
        {
            return Array.Empty<BillingPlan>();
        }

        return products
            .Select(envelope => envelope.Product)
            .Where(product => product is not null && product.ArchivedAt is null)
            .Select(product => MapPlan(product!))
            .ToArray();
    }

    /// <summary>
    /// Resolves a plan that is genuinely subscribable: it must exist, not be archived, and live
    /// in the configured product family. Anything else is reported as unresolved so callers raise
    /// a configuration error rather than enrolling against an unexpected plan.
    /// </summary>
    public async Task<BillingPlan?> FindPlanByHandleAsync(string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        var path = $"products/handle/{EscapeHandle(planHandle)}.json";

        var envelope = await SendOrNullAsync<MaxioProductEnvelope>(HttpMethod.Get, path, null,
            $"resolve plan '{planHandle}'", cancellationToken);

        var product = envelope?.Product;
        if (product is null || product.ArchivedAt is not null)
        {
            return null;
        }

        var configuredFamily = _settings.ProductFamilyHandle;
        if (!string.IsNullOrWhiteSpace(configuredFamily)
            && !string.Equals(product.ProductFamily?.Handle, configuredFamily, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                $"Plan '{planHandle}' belongs to product family '{product.ProductFamily?.Handle}', " +
                $"not the configured '{configuredFamily}'; treating it as unavailable.");
            return null;
        }

        return MapPlan(product);
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string customerReference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return null;
        }

        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(customerReference)}";

        var envelope = await SendOrNullAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, null,
            "look up customer", cancellationToken);

        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(string customerReference,
        string email,
        string? firstName,
        string? lastName,
        CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        // Maxio requires a first and last name. eShopOnWeb identities carry only an email, so
        // derive a stable, non-fabricated placeholder rather than inventing personal details.
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = string.IsNullOrWhiteSpace(firstName) ? DeriveFirstName(email) : firstName,
                LastName = string.IsNullOrWhiteSpace(lastName) ? "eShopOnWeb" : lastName,
                Email = email,
                Reference = customerReference
            }
        };

        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", request,
            "create customer", cancellationToken);

        var customer = envelope?.Customer
            ?? throw new BillingProviderException("Maxio accepted the customer but returned no customer record.");

        return MapCustomer(customer);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(string customerReference,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = planHandle,
                CustomerReference = customerReference,
                PaymentCollectionMethod = RemittancePaymentCollection
            }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request,
            $"subscribe '{customerReference}' to '{planHandle}'", cancellationToken);

        return RequireSubscription(envelope, "create the subscription");
    }

    public async Task<BillingSubscription?> GetSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var envelope = await SendOrNullAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get,
            $"subscriptions/{subscriptionId}.json", null,
            $"read subscription {subscriptionId}", cancellationToken);

        return envelope?.Subscription is null ? null : MapSubscription(envelope.Subscription);
    }

    public async Task<IReadOnlyCollection<BillingSubscription>> ListSubscriptionsForCustomerAsync(int customerId,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json", null,
            $"list subscriptions for customer {customerId}", cancellationToken);

        if (subscriptions is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        return subscriptions
            .Select(envelope => envelope.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => MapSubscription(subscription!))
            .ToArray();
    }

    public async Task<MeteredComponent?> FindComponentByHandleAsync(string componentHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(componentHandle))
        {
            return null;
        }

        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var path = $"product_families/{familyId}/components/handle:{EscapeHandle(componentHandle)}.json";

        var envelope = await SendOrNullAsync<MaxioComponentEnvelope>(HttpMethod.Get, path, null,
            $"resolve component '{componentHandle}'", cancellationToken);

        return envelope?.Component is null ? null : MapComponent(envelope.Component);
    }

    public async Task<UsageRecord> RecordUsageAsync(int subscriptionId,
        string componentHandle,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateUsageRequest
        {
            Usage = new MaxioCreateUsage { Quantity = quantity, Memo = memo }
        };

        var path = $"subscriptions/{subscriptionId}/components/{ComponentSegment(componentHandle)}/usages.json";

        var envelope = await SendAsync<MaxioUsageEnvelope>(HttpMethod.Post, path, request,
            $"record usage on subscription {subscriptionId}", cancellationToken);

        var usage = envelope?.Usage
            ?? throw new BillingProviderException("Maxio accepted the usage but returned no usage record.");

        return new UsageRecord(usage.Id, usage.SubscriptionId == 0 ? subscriptionId : usage.SubscriptionId,
            string.IsNullOrEmpty(usage.ComponentHandle) ? componentHandle : usage.ComponentHandle,
            usage.Quantity)
        {
            Memo = usage.Memo
        };
    }

    public async Task<decimal?> GetPeriodToDateUsageAsync(int subscriptionId,
        string componentHandle,
        CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/{subscriptionId}/components/{ComponentSegment(componentHandle)}.json";

        var envelope = await SendOrNullAsync<MaxioSubscriptionComponentEnvelope>(HttpMethod.Get, path, null,
            $"read usage total on subscription {subscriptionId}", cancellationToken);

        return envelope?.Component?.UnitBalance;
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        var request = new MaxioMigrationRequest
        {
            Migration = new MaxioMigration { ProductHandle = targetPlanHandle }
        };

        var envelope = await SendAsync<MaxioMigrationPreviewEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/migrations/preview.json", request,
            $"preview a plan change on subscription {subscriptionId}", cancellationToken);

        var preview = envelope?.Migration
            ?? throw new BillingProviderException("Maxio returned no proration preview.");

        return new PlanChangePreview(targetPlanHandle,
            ToDecimalAmount(preview.ProratedAdjustmentInCents),
            ToDecimalAmount(preview.ChargeInCents),
            ToDecimalAmount(preview.PaymentDueInCents),
            ToDecimalAmount(preview.CreditAppliedInCents));
    }

    public async Task<BillingSubscription> ChangePlanAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            // A delayed product change takes effect at the next renewal and is never prorated.
            var delayed = new MaxioUpdateSubscriptionRequest
            {
                Subscription = new MaxioUpdateSubscription
                {
                    ProductHandle = targetPlanHandle,
                    ProductChangeDelayed = true
                }
            };

            var scheduled = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Put,
                $"subscriptions/{subscriptionId}.json", delayed,
                $"schedule a plan change on subscription {subscriptionId}", cancellationToken);

            return RequireSubscription(scheduled, "schedule the plan change");
        }

        var migration = new MaxioMigrationRequest
        {
            Migration = new MaxioMigration { ProductHandle = targetPlanHandle }
        };

        var migrated = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/migrations.json", migration,
            $"change the plan on subscription {subscriptionId}", cancellationToken);

        return RequireSubscription(migrated, "change the plan");
    }

    public async Task<BillingSubscription> PauseAsync(int subscriptionId,
        DateTimeOffset? automaticallyResumeAt,
        CancellationToken cancellationToken = default)
    {
        var request = new MaxioHoldRequest
        {
            Hold = new MaxioHoldOptions { AutomaticallyResumeAt = automaticallyResumeAt }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/hold.json", request,
            $"pause subscription {subscriptionId}", cancellationToken);

        return RequireSubscription(envelope, "pause the subscription");
    }

    public async Task<BillingSubscription> ResumeAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/resume.json", null,
            $"resume subscription {subscriptionId}", cancellationToken);

        return RequireSubscription(envelope, "resume the subscription");
    }

    public async Task<BillingSubscription> CancelAsync(int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var request = new MaxioCancellationRequest
        {
            Subscription = new MaxioCancellationOptions { CancellationMessage = reason }
        };

        if (timing == CancellationTiming.EndOfBillingPeriod)
        {
            // This endpoint answers with a bare confirmation message rather than the subscription,
            // so read the subscription back to report its real post-request state.
            await SendAsync<JsonElement?>(HttpMethod.Post,
                $"subscriptions/{subscriptionId}/delayed_cancel.json", request,
                $"schedule cancellation of subscription {subscriptionId}", cancellationToken);

            var refreshed = await GetSubscriptionAsync(subscriptionId, cancellationToken);

            return refreshed ?? throw new BillingProviderException(
                $"Maxio scheduled the cancellation of subscription {subscriptionId} but the subscription " +
                "could no longer be read back.");
        }

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Delete,
            $"subscriptions/{subscriptionId}.json", request,
            $"cancel subscription {subscriptionId}", cancellationToken);

        return RequireSubscription(envelope, "cancel the subscription");
    }

    public async Task<BillingSubscription> ReactivateAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        // The reactivate body is not enveloped and every field is optional.
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Put,
            $"subscriptions/{subscriptionId}/reactivate.json", new { },
            $"reactivate subscription {subscriptionId}", cancellationToken);

        return RequireSubscription(envelope, "reactivate the subscription");
    }

    /// <summary>
    /// Resolves the configured product family's numeric id. Maxio assigns these ids and reassigns
    /// them whenever a site is re-seeded, so the durable handle is always the starting point.
    /// </summary>
    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_productFamilyId.HasValue)
        {
            return _productFamilyId.Value;
        }

        var families = await SendAsync<List<MaxioProductFamilyEnvelope>>(HttpMethod.Get,
            "product_families.json", null, "list product families", cancellationToken);

        var match = families?
            .Select(envelope => envelope.ProductFamily)
            .FirstOrDefault(family => string.Equals(family?.Handle, _settings.ProductFamilyHandle,
                StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            throw new BillingConfigurationException(
                $"The configured product family '{_settings.ProductFamilyHandle}' does not exist on Maxio site " +
                $"'{_settings.Subdomain}'. Re-run the billing provider seed.");
        }

        _productFamilyId = match.Id;

        return match.Id;
    }

    private async Task<TResponse?> SendAsync<TResponse>(HttpMethod method,
        string path,
        object? body,
        string operation,
        CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, path, body, operation, cancellationToken);

        await EnsureSuccessAsync(response, operation, cancellationToken);

        return await DeserializeAsync<TResponse>(response, operation, cancellationToken);
    }

    /// <summary>Same as <see cref="SendAsync{TResponse}"/>, but a 404 yields <c>null</c> instead of throwing.</summary>
    private async Task<TResponse?> SendOrNullAsync<TResponse>(HttpMethod method,
        string path,
        object? body,
        string operation,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        using var response = await SendCoreAsync(method, path, body, operation, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, operation, cancellationToken);

        return await DeserializeAsync<TResponse>(response, operation, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method,
        string path,
        object? body,
        string operation,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: SerializerOptions);
        }

        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new BillingProviderException(
                $"The billing provider could not be reached to {operation}.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException($"The billing provider timed out trying to {operation}.", exception);
        }
    }

    private static async Task<TResponse?> DeserializeAsync<TResponse>(HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new BillingProviderException(
                $"The billing provider returned a response that could not be read while trying to {operation}.",
                exception);
        }
    }

    /// <summary>
    /// Turns every provider failure into a typed <see cref="BillingProviderException"/> so no
    /// caller ever has to reason about HTTP status codes or Maxio's several error shapes.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = (int)response.StatusCode;
        var payload = await ReadBodySafelyAsync(response, cancellationToken);
        var errors = ParseProviderErrors(payload);
        var detail = errors.Count > 0 ? string.Join(" ", errors) : response.ReasonPhrase;

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new BillingProviderAuthenticationException(
                    $"The billing provider rejected this integration's credentials while trying to {operation}.",
                    status),

            HttpStatusCode.NotFound =>
                new BillingProviderNotFoundException(
                    $"The billing provider could not find what was needed to {operation}.", status),

            HttpStatusCode.BadRequest or HttpStatusCode.Conflict or (HttpStatusCode)422 =>
                new BillingProviderValidationException(
                    $"The billing provider refused to {operation}: {detail}", status, errors),

            _ => new BillingProviderException(
                $"The billing provider failed to {operation} (HTTP {status}): {detail}", status, errors)
        };
    }

    private static async Task<string> ReadBodySafelyAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            // A body we cannot read must not mask the status code we already have.
            return string.Empty;
        }
    }

    /// <summary>
    /// Maxio reports errors in several shapes: <c>{"errors": ["..."]}</c>,
    /// <c>{"errors": {"field": "..."}}</c>, a singular <c>{"error": "..."}</c>, and plain text for
    /// authentication failures. All of them are flattened to a list of messages here.
    /// </summary>
    private static IReadOnlyCollection<string> ParseProviderErrors(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var messages = new List<string>();

            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                AddMessage(messages, document.RootElement);
                return messages;
            }

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<string>();
            }

            if (document.RootElement.TryGetProperty("error", out var singular))
            {
                AddMessage(messages, singular);
            }

            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                CollectMessages(messages, errors);
            }

            return messages;
        }
        catch (JsonException)
        {
            // Not JSON at all — Maxio answers a bad API key with a plain-text body.
            return new[] { payload.Trim() };
        }
    }

    private static void CollectMessages(List<string> messages, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                AddMessage(messages, element);
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectMessages(messages, item);
                }

                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectMessages(messages, property.Value);
                }

                break;
        }
    }

    private static void AddMessage(List<string> messages, JsonElement element)
    {
        var message = element.GetString();
        if (!string.IsNullOrWhiteSpace(message))
        {
            messages.Add(message.Trim());
        }
    }

    private static BillingSubscription RequireSubscription(MaxioSubscriptionEnvelope? envelope, string operation)
    {
        var subscription = envelope?.Subscription
            ?? throw new BillingProviderException(
                $"Maxio reported success but returned no subscription when asked to {operation}.");

        return MapSubscription(subscription);
    }

    private static BillingPlan MapPlan(MaxioProduct product) =>
        new BillingPlan(product.Id,
            product.Handle ?? string.Empty,
            product.Name ?? product.Handle ?? string.Empty,
            ToDecimalAmount(product.PriceInCents),
            product.Interval,
            product.IntervalUnit ?? string.Empty)
        {
            Description = product.Description,
            ProductFamilyHandle = product.ProductFamily?.Handle,
            RequiresPaymentMethod = product.RequireCreditCard,
            IsArchived = product.ArchivedAt is not null
        };

    private static BillingCustomer MapCustomer(MaxioCustomer customer) =>
        new BillingCustomer(customer.Id, customer.Reference, customer.Email ?? string.Empty)
        {
            FirstName = customer.FirstName,
            LastName = customer.LastName
        };

    private static BillingSubscription MapSubscription(MaxioSubscription subscription)
    {
        var providerState = subscription.State ?? string.Empty;

        return new BillingSubscription(subscription.Id, MapStatus(providerState),
            string.IsNullOrEmpty(providerState) ? "unknown" : providerState)
        {
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PlanPrice = ToDecimalAmount(subscription.ProductPriceInCents),
            Balance = ToDecimalAmount(subscription.BalanceInCents),
            CustomerId = subscription.Customer?.Id ?? 0,
            CustomerReference = subscription.Customer?.Reference,
            CurrentPeriodStartsAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod ?? false,
            DelayedCancelAt = subscription.DelayedCancelAt,
            NextPlanHandle = subscription.NextProductHandle
        };
    }

    private static MeteredComponent MapComponent(MaxioComponent component) =>
        new MeteredComponent(component.Id,
            component.Handle ?? string.Empty,
            component.Name ?? component.Handle ?? string.Empty,
            component.Kind ?? string.Empty)
        {
            IsMetered = string.Equals(component.Kind, MaxioComponentKinds.Metered, StringComparison.OrdinalIgnoreCase),
            UnitPrice = ParseUnitPrice(component.UnitPrice),
            UnitName = component.UnitName,
            PricingScheme = component.PricingScheme
        };

    /// <summary>
    /// Maps Maxio's lifecycle vocabulary onto the domain's. Note that Maxio's customer-facing
    /// pause state is <c>on_hold</c>; its <c>paused</c> state means the account is in arrears and
    /// is deliberately kept distinct.
    /// </summary>
    private static SubscriptionStatus MapStatus(string state) => state.ToLowerInvariant() switch
    {
        "active" => SubscriptionStatus.Active,
        "trialing" => SubscriptionStatus.Trialing,
        "pending" or "assessing" or "awaiting_signup" => SubscriptionStatus.Pending,
        "on_hold" => SubscriptionStatus.OnHold,
        "paused" => SubscriptionStatus.Paused,
        "past_due" or "soft_failure" => SubscriptionStatus.PastDue,
        "unpaid" => SubscriptionStatus.Unpaid,
        "suspended" => SubscriptionStatus.Suspended,
        "canceled" => SubscriptionStatus.Canceled,
        "expired" => SubscriptionStatus.Expired,
        "trial_ended" => SubscriptionStatus.TrialEnded,
        "failed_to_create" => SubscriptionStatus.Failed,
        _ => SubscriptionStatus.Unknown
    };

    /// <summary>Converts Maxio's integer minor units (cents) to whole currency units (dollars).</summary>
    private static decimal ToDecimalAmount(long amountInCents) => amountInCents / 100m;

    private static decimal? ParseUnitPrice(string? unitPrice) =>
        decimal.TryParse(unitPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string DeriveFirstName(string email)
    {
        var localPart = email.Split('@')[0];

        return string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;
    }

    /// <summary>
    /// Maxio addresses a component either by numeric id or by a <c>handle:</c>-prefixed segment.
    /// The colon is a legal path character and must not be escaped.
    /// </summary>
    private static string ComponentSegment(string componentHandle) =>
        $"handle:{EscapeHandle(componentHandle)}";

    private static string EscapeHandle(string handle) => Uri.EscapeDataString(handle);

    private static AuthenticationHeaderValue BuildAuthenticationHeader(string apiKey)
    {
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{apiKey}:{ApiKeyPasswordPlaceholder}"));

        return new AuthenticationHeaderValue("Basic", credentials);
    }
}
