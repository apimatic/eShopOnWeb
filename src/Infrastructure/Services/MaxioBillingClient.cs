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
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single integration point with Maxio Advanced Billing (§2.2/§4.2). Everything the rest of the
/// application knows about the provider stops here: this class speaks HTTP and JSON, and hands back
/// only the provider-agnostic types of <see cref="IBillingClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// The outbound target is configuration-driven. <see cref="MaxioSettings.ResolveBaseUrl"/> honours an
/// explicit <c>Maxio:BaseUrl</c> verbatim and only derives the host from the subdomain when none is
/// set, so the same build can target production, a sandbox tenant, or a local mock server (§2.3).
/// The base address is applied by the composition root when the typed client is registered; this
/// class applies it defensively as well so it behaves identically when constructed directly.
/// </para>
/// <para>
/// Maxio assigns numeric IDs and reassigns them whenever a catalog is re-created, so every entity is
/// resolved from its stable handle rather than a configured ID (§1.3).
/// </para>
/// </remarks>
public class MaxioBillingClient : IBillingClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    /// <summary>
    /// Handle-to-ID resolutions for the lifetime of this client instance. Typed clients are
    /// short-lived, so this saves repeat lookups within one operation without ever serving a stale
    /// ID across a re-seed.
    /// </summary>
    private long? _productFamilyId;
    private BillingComponent? _usageComponent;

    public MaxioBillingClient(HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingClient> logger)
    {
        _httpClient = Guard.Against.Null(httpClient, nameof(httpClient));
        _settings = Guard.Against.Null(settings, nameof(settings)).Value;
        _logger = Guard.Against.Null(logger, nameof(logger));

        _httpClient.BaseAddress ??= _settings.ResolveBaseUrl();

        if (_httpClient.DefaultRequestHeaders.Authorization is null && !string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            // Maxio authenticates with HTTP Basic: the API key is the username and the password is
            // the literal "x".
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        if (!_httpClient.DefaultRequestHeaders.Accept.Any())
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    public async Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        var products = await SendAsync<List<ProductEnvelope>>(
            HttpMethod.Get, $"/product_families/{familyId}/products.json", body: null, cancellationToken)
            ?? new List<ProductEnvelope>();

        return products
            .Select(p => p.Product)
            .Where(p => p is not null && p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => MapPlan(p!))
            .ToList();
    }

    public async Task<BillingPlan?> FindPlanByHandleAsync(string productHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(productHandle, nameof(productHandle));

        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(reference, nameof(reference));

        // Maxio answers 404 when no customer carries the reference, which is a normal "not found",
        // not a failure.
        var envelope = await SendOptionalAsync<CustomerEnvelope>(
            HttpMethod.Get, $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}", body: null, cancellationToken);

        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(EnsureCustomerRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request, nameof(request));

        var existing = await FindCustomerByReferenceAsync(request.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var payload = new CreateCustomerEnvelope
        {
            Customer = new CreateCustomerPayload
            {
                Email = request.Email,
                Reference = request.Reference,
                // Maxio requires a name; fall back to the reference so the record is never nameless.
                FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? request.Reference : request.FirstName,
                LastName = string.IsNullOrWhiteSpace(request.LastName) ? "eShopOnWeb" : request.LastName
            }
        };

        var created = await SendAsync<CustomerEnvelope>(HttpMethod.Post, "/customers.json", payload, cancellationToken);
        if (created?.Customer is null)
        {
            throw new BillingProviderException("Maxio accepted the customer creation but returned no customer record.");
        }

        return MapCustomer(created.Customer);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request, nameof(request));

        var payload = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionPayload
            {
                ProductHandle = request.ProductHandle,
                CustomerId = request.CustomerId,
                PaymentCollectionMethod = string.IsNullOrWhiteSpace(request.PaymentCollectionMethod)
                    ? NullIfBlank(_settings.PaymentCollectionMethod)
                    : request.PaymentCollectionMethod
            }
        };

        var created = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post, "/subscriptions.json", payload, cancellationToken);
        return RequireSubscription(created, "creating a subscription");
    }

    public async Task<BillingSubscription?> GetSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await SendOptionalAsync<SubscriptionEnvelope>(
            HttpMethod.Get, $"/subscriptions/{subscriptionId}.json", body: null, cancellationToken);

        return envelope?.Subscription is null ? null : MapSubscription(envelope.Subscription);
    }

    public async Task<IReadOnlyCollection<BillingSubscription>> ListSubscriptionsForCustomerAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var envelopes = await SendOptionalAsync<List<SubscriptionEnvelope>>(
            HttpMethod.Get, $"/customers/{customerId}/subscriptions.json", body: null, cancellationToken);

        if (envelopes is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        return envelopes
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    public async Task<BillingComponent?> FindComponentByHandleAsync(string componentHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(componentHandle, nameof(componentHandle));

        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        var components = await SendAsync<List<ComponentEnvelope>>(
            HttpMethod.Get, $"/product_families/{familyId}/components.json", body: null, cancellationToken)
            ?? new List<ComponentEnvelope>();

        var match = components
            .Select(c => c.Component)
            .FirstOrDefault(c => c is not null
                && !c.Archived
                && string.Equals(c.Handle, componentHandle, StringComparison.OrdinalIgnoreCase));

        return match is null ? null : MapComponent(match);
    }

    public async Task<BillingComponent> GetUsageComponentAsync(CancellationToken cancellationToken = default)
    {
        if (_usageComponent is not null)
        {
            return _usageComponent;
        }

        var handle = _settings.MeteredComponentHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingConfigurationException(
                "Maxio:MeteredComponentHandle is not configured, so pay-as-you-go usage cannot be recorded.");
        }

        var component = await FindComponentByHandleAsync(handle, cancellationToken)
            ?? throw new BillingConfigurationException(
                $"The configured usage component '{handle}' does not exist on product family '{_settings.ProductFamilyHandle}'. Re-seed the sandbox (UC0) before recording usage.");

        // A component's kind cannot be converted in place, so a mismatch is always a seeding error.
        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"The configured usage component '{handle}' is of kind '{component.Kind}', not metered. Archive it and recreate it as a metered component (UC0) before recording usage.");
        }

        _usageComponent = component;
        return component;
    }

    public async Task<long> RecordUsageAsync(RecordUsageRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request, nameof(request));

        var payload = new CreateUsageEnvelope
        {
            Usage = new CreateUsagePayload { Quantity = request.Quantity, Memo = request.Memo }
        };

        var created = await SendAsync<UsageEnvelope>(
            HttpMethod.Post,
            $"/subscriptions/{request.SubscriptionId}/components/{request.ComponentId}/usages.json",
            payload,
            cancellationToken);

        if (created?.Usage is null)
        {
            throw new BillingProviderException("Maxio accepted the usage record but returned no usage receipt.");
        }

        return created.Usage.Id;
    }

    public async Task<int?> GetPeriodToDateUnitsAsync(long subscriptionId, long componentId, CancellationToken cancellationToken = default)
    {
        // 404 here means the component has never accrued usage on this subscription.
        var envelope = await SendOptionalAsync<SubscriptionComponentEnvelope>(
            HttpMethod.Get, $"/subscriptions/{subscriptionId}/components/{componentId}.json", body: null, cancellationToken);

        return envelope?.Component?.UnitBalance;
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(long subscriptionId,
        string targetProductHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(targetProductHandle, nameof(targetProductHandle));

        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            // Deferring to the next renewal prorates nothing: the customer owes nothing now and is
            // billed the new plan's price from the next period. Maxio's migration preview models an
            // immediate migration only, so the deferred figures are derived from the target plan.
            var plan = await FindPlanByHandleAsync(targetProductHandle, cancellationToken)
                ?? throw new BillingConfigurationException(
                    $"Plan '{targetProductHandle}' does not exist on the billing provider. Re-seed the sandbox (UC0) or correct the configured plan handles.");

            return new PlanChangePreview(targetProductHandle, timing,
                proratedAdjustmentInCents: 0,
                chargeInCents: plan.PriceInCents,
                paymentDueInCents: 0,
                creditAppliedInCents: 0);
        }

        var payload = new MigrationEnvelope { Migration = new MigrationPayload { ProductHandle = targetProductHandle } };

        var preview = await SendAsync<MigrationPreviewEnvelope>(
            HttpMethod.Post, $"/subscriptions/{subscriptionId}/migrations/preview.json", payload, cancellationToken);

        if (preview?.Migration is null)
        {
            throw new BillingProviderException("Maxio returned no proration preview for this plan change.");
        }

        return new PlanChangePreview(targetProductHandle, timing,
            preview.Migration.ProratedAdjustmentInCents,
            preview.Migration.ChargeInCents,
            preview.Migration.PaymentDueInCents,
            preview.Migration.CreditAppliedInCents);
    }

    public async Task<BillingSubscription> ChangePlanAsync(long subscriptionId,
        string targetProductHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(targetProductHandle, nameof(targetProductHandle));

        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            var deferred = new UpdateSubscriptionEnvelope
            {
                Subscription = new UpdateSubscriptionPayload
                {
                    ProductHandle = targetProductHandle,
                    ProductChangeDelayed = true
                }
            };

            var scheduled = await SendAsync<SubscriptionEnvelope>(
                HttpMethod.Put, $"/subscriptions/{subscriptionId}.json", deferred, cancellationToken);

            return RequireSubscription(scheduled, "scheduling a plan change");
        }

        var payload = new MigrationEnvelope { Migration = new MigrationPayload { ProductHandle = targetProductHandle } };

        var migrated = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Post, $"/subscriptions/{subscriptionId}/migrations.json", payload, cancellationToken);

        return RequireSubscription(migrated, "changing plan");
    }

    public async Task<BillingSubscription> PauseAsync(long subscriptionId, CancellationToken cancellationToken = default)
    {
        var held = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Post, $"/subscriptions/{subscriptionId}/hold.json", body: null, cancellationToken);

        return RequireSubscription(held, "pausing a subscription");
    }

    public async Task<BillingSubscription> ResumeAsync(long subscriptionId, CancellationToken cancellationToken = default)
    {
        var resumed = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Post, $"/subscriptions/{subscriptionId}/resume.json", body: null, cancellationToken);

        return RequireSubscription(resumed, "resuming a subscription");
    }

    public async Task<BillingSubscription> CancelAsync(long subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var payload = new CancelSubscriptionEnvelope
        {
            Subscription = new CancelSubscriptionPayload { CancellationMessage = NullIfBlank(reason) }
        };

        // An end-of-period cancel schedules the cancellation at the period boundary and leaves the
        // subscription running until then; an immediate cancel stops it now.
        if (timing == CancellationTiming.EndOfPeriod)
        {
            // Unlike every other lifecycle route, this one answers with a bare confirmation message
            // rather than the subscription, so the updated record has to be read back.
            await SendAsync<MaxioMessageResponse>(
                HttpMethod.Post, $"/subscriptions/{subscriptionId}/delayed_cancel.json", payload, cancellationToken);

            return await GetSubscriptionAsync(subscriptionId, cancellationToken)
                ?? throw new BillingProviderException(
                    $"Subscription {subscriptionId} could not be read back after scheduling an end-of-period cancellation.");
        }

        var cancelled = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Delete, $"/subscriptions/{subscriptionId}.json", payload, cancellationToken);

        return RequireSubscription(cancelled, "cancelling a subscription");
    }

    public async Task<BillingSubscription> ReactivateAsync(long subscriptionId, CancellationToken cancellationToken = default)
    {
        var reactivated = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Put, $"/subscriptions/{subscriptionId}/reactivate.json", body: null, cancellationToken);

        return RequireSubscription(reactivated, "reactivating a subscription");
    }

    private async Task<long> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_productFamilyId.HasValue)
        {
            return _productFamilyId.Value;
        }

        var handle = _settings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingConfigurationException(
                "Maxio:ProductFamilyHandle is not configured, so the plan catalog cannot be resolved.");
        }

        var families = await SendAsync<List<ProductFamilyEnvelope>>(
            HttpMethod.Get, "/product_families.json", body: null, cancellationToken)
            ?? new List<ProductFamilyEnvelope>();

        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null
                && f.ArchivedAt is null
                && string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase))
            ?? throw new BillingConfigurationException(
                $"Product family '{handle}' does not exist on the billing provider. Seed it (UC0) or correct Maxio:ProductFamilyHandle.");

        _productFamilyId = family.Id;
        return family.Id;
    }

    /// <summary>Issues a request, treating a 404 as a hard failure.</summary>
    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        => await SendCoreAsync<T>(method, path, body, treatNotFoundAsNull: false, cancellationToken);

    /// <summary>Issues a request where a 404 is a legitimate "not found" and yields <c>null</c>.</summary>
    private async Task<T?> SendOptionalAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        => await SendCoreAsync<T>(method, path, body, treatNotFoundAsNull: true, cancellationToken);

    private async Task<T?> SendCoreAsync<T>(HttpMethod method,
        string path,
        object? body,
        bool treatNotFoundAsNull,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: SerializerOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A caller-initiated cancellation is not a provider failure.
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new BillingProviderException($"The billing provider did not respond in time for {method} {path}.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"The billing provider could not be reached for {method} {path}.", ex);
        }

        using (response)
        {
            if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errors = await ReadProviderErrorsAsync(response, cancellationToken);
                var detail = errors.Count > 0 ? string.Join("; ", errors) : response.ReasonPhrase ?? "no detail supplied";

                _logger.LogWarning("Maxio rejected {0} {1} with {2}: {3}", method, path, (int)response.StatusCode, detail);

                throw new BillingProviderException(
                    $"The billing provider rejected {method} {path} with status {(int)response.StatusCode}: {detail}",
                    (int)response.StatusCode,
                    errors);
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return default;
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new BillingProviderException(
                    $"The billing provider returned a response for {method} {path} that could not be understood.", ex);
            }
        }
    }

    /// <summary>
    /// Extracts the provider's error messages. Maxio reports them as a string array, but also uses a
    /// field-keyed object for validation failures, so both shapes are handled.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadProviderErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var messages = new List<string>();

        string payload;
        try
        {
            payload = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            return messages;
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return messages;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("errors", out var errors))
            {
                return messages;
            }

            switch (errors.ValueKind)
            {
                case JsonValueKind.String:
                    AddIfPresent(messages, errors.GetString());
                    break;

                case JsonValueKind.Array:
                    foreach (var item in errors.EnumerateArray())
                    {
                        AddIfPresent(messages, item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString());
                    }

                    break;

                case JsonValueKind.Object:
                    foreach (var field in errors.EnumerateObject())
                    {
                        if (field.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in field.Value.EnumerateArray())
                            {
                                AddIfPresent(messages, $"{field.Name}: {item}");
                            }
                        }
                        else
                        {
                            AddIfPresent(messages, $"{field.Name}: {field.Value}");
                        }
                    }

                    break;
            }
        }
        catch (JsonException)
        {
            // A non-JSON error body (a gateway HTML page, for example) carries nothing useful.
        }

        return messages;
    }

    private static void AddIfPresent(ICollection<string> messages, string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            messages.Add(message.Trim());
        }
    }

    private static BillingSubscription RequireSubscription(SubscriptionEnvelope? envelope, string operation)
    {
        if (envelope?.Subscription is null)
        {
            throw new BillingProviderException($"Maxio returned no subscription record after {operation}.");
        }

        return MapSubscription(envelope.Subscription);
    }

    private static BillingPlan MapPlan(ProductResource product) => new(
        product.Id,
        product.Handle!,
        product.Name ?? product.Handle!,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit ?? string.Empty,
        product.ProductFamily?.Handle ?? string.Empty,
        product.RequireCreditCard,
        product.Description);

    private static BillingCustomer MapCustomer(CustomerResource customer) => new(
        customer.Id,
        customer.Reference ?? customer.Email ?? customer.Id.ToString(CultureInfo.InvariantCulture),
        customer.Email ?? string.Empty,
        customer.FirstName,
        customer.LastName);

    private static BillingComponent MapComponent(ComponentResource component) => new(
        component.Id,
        component.Handle!,
        component.Name ?? component.Handle!,
        component.Kind ?? string.Empty,
        component.UnitName,
        ParseUnitPrice(component.UnitPrice),
        component.PricingScheme ?? string.Empty);

    private static BillingSubscription MapSubscription(SubscriptionResource subscription) => new(
        subscription.Id,
        MapState(subscription.State),
        subscription.Customer?.Id ?? 0,
        subscription.Customer?.Reference,
        subscription.Product?.Handle ?? string.Empty,
        subscription.Product?.Name ?? string.Empty,
        subscription.ProductPriceInCents,
        subscription.CurrentPeriodStartedAt,
        subscription.CurrentPeriodEndsAt,
        subscription.NextAssessmentAt,
        subscription.CancelAtEndOfPeriod,
        subscription.DelayedCancelAt,
        subscription.NextProductHandle,
        subscription.BalanceInCents,
        subscription.Currency ?? "USD");

    /// <summary>
    /// Parses a component's per-unit price. Unlike the <c>*_in_cents</c> fields, Maxio reports this
    /// as a decimal string in major units, so it is parsed with the invariant culture to keep the
    /// decimal separator from being misread under a comma-decimal locale.
    /// </summary>
    private static decimal? ParseUnitPrice(string? unitPrice)
        => decimal.TryParse(unitPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static SubscriptionState MapState(string? state) => state?.Trim().ToLowerInvariant() switch
    {
        "trialing" => SubscriptionState.Trialing,
        "active" or "assessing" => SubscriptionState.Active,
        // Maxio calls a paused subscription "on_hold".
        "on_hold" or "paused" => SubscriptionState.Paused,
        "past_due" => SubscriptionState.PastDue,
        "soft_failure" => SubscriptionState.SoftFailure,
        "unpaid" => SubscriptionState.Unpaid,
        "canceled" or "cancelled" => SubscriptionState.Canceled,
        "expired" or "trial_ended" => SubscriptionState.Expired,
        "failed_to_create" => SubscriptionState.Failed,
        "suspended" => SubscriptionState.Suspended,
        "pending" or "awaiting_signup" => SubscriptionState.Pending,
        _ => SubscriptionState.Unknown
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
