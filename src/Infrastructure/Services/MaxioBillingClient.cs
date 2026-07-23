using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
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
/// The one class in eShopOnWeb that talks to Maxio Advanced Billing (§2.2). It speaks the provider's
/// REST API over a typed <see cref="HttpClient"/>, normalizes every result into the provider-agnostic
/// types of <see cref="IBillingClient"/>, and surfaces every failure as
/// <see cref="BillingProviderException"/>.
///
/// Two conventions worth knowing when reading this file:
/// <list type="bullet">
/// <item>Handles, not ids, address entities wherever the API allows it (<c>handle:</c> prefixes and
/// <c>product_handle</c>). The provider reassigns numeric ids when a catalog is re-seeded, so the
/// handle is the only durable identifier (§1.3).</item>
/// <item>Money crosses the wire in two different shapes: products and migrations use integer minor
/// units (<c>price_in_cents</c>), components use a decimal string in whole units
/// (<c>unit_price: "0.01"</c>). Both are converted here so the domain only ever sees whole units.</item>
/// </list>
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private const int MinorUnitsPerUnit = 100;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly MaxioComponentValidationCache _validationCache;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    private int? _productFamilyId;

    public MaxioBillingClient(HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        MaxioComponentValidationCache validationCache,
        IAppLogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _validationCache = validationCache;
        _logger = logger;

        // The API key authenticates as the Basic username with the literal password "x".
        if (_httpClient.DefaultRequestHeaders.Authorization is null && !string.IsNullOrEmpty(_settings.ApiKey))
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public string MeteredComponentHandle => _settings.MeteredComponentHandle;

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = FamilyReference();
        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get,
            $"product_families/{family}/products.json", null, cancellationToken);

        if (envelopes is null)
        {
            return Array.Empty<SubscriptionPlan>();
        }

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => MapPlan(p!))
            .ToList();
    }

    public async Task<SubscriptionPlan?> GetPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        // Resolved through the family listing rather than the site-wide handle lookup, so a handle
        // that exists elsewhere on the site but not in the configured family correctly fails to
        // resolve (UC1 / UC3 configuration-error paths).
        var plans = await ListPlansAsync(cancellationToken);

        return plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string userReference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userReference))
        {
            return null;
        }

        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(userReference)}", null, cancellationToken,
            allowNotFound: true);

        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(string userReference, string email, string firstName,
        string lastName, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = userReference
            }
        };

        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", request, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new BillingProviderException($"Maxio did not return a customer when creating '{userReference}'.");
        }

        return MapCustomer(envelope.Customer);
    }

    public async Task<Subscription> CreateSubscriptionAsync(string userReference, string planHandle,
        CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = planHandle,
                CustomerReference = userReference,
                PaymentCollectionMethod = _settings.PaymentCollectionMethod
            }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request,
            cancellationToken);

        return RequireSubscription(envelope, $"creating a subscription for '{userReference}' on plan '{planHandle}'");
    }

    public async Task<IReadOnlyCollection<Subscription>> ListSubscriptionsAsync(string userReference,
        CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            // A user who has never subscribed has no provider-side customer; that is not an error.
            return Array.Empty<Subscription>();
        }

        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get,
            $"customers/{customer.Id}/subscriptions.json", null, cancellationToken);

        if (envelopes is null)
        {
            return Array.Empty<Subscription>();
        }

        return envelopes
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    public async Task<Subscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get,
            $"subscriptions/{subscriptionId}.json", null, cancellationToken, allowNotFound: true);

        return envelope?.Subscription is null ? null : MapSubscription(envelope.Subscription);
    }

    public async Task<MeteredComponent?> GetComponentByHandleAsync(string componentHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(componentHandle))
        {
            return null;
        }

        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var envelope = await SendAsync<MaxioComponentEnvelope>(HttpMethod.Get,
            $"product_families/{familyId}/components/handle:{Uri.EscapeDataString(componentHandle)}.json", null,
            cancellationToken, allowNotFound: true);

        return envelope?.Component is null ? null : MapComponent(envelope.Component);
    }

    public async Task<UsageRecord> RecordUsageAsync(int subscriptionId, string componentHandle, decimal quantity,
        string? memo, CancellationToken cancellationToken = default)
    {
        await EnsureMeteredComponentAsync(componentHandle, cancellationToken);

        var request = new MaxioCreateUsageRequest
        {
            Usage = new MaxioCreateUsage { Quantity = quantity, Memo = memo }
        };

        var envelope = await SendAsync<MaxioUsageEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/components/handle:{Uri.EscapeDataString(componentHandle)}/usages.json",
            request, cancellationToken);

        if (envelope?.Usage is null)
        {
            throw new BillingProviderException(
                $"Maxio did not return a usage record for subscription {subscriptionId}.");
        }

        var usage = envelope.Usage;

        return new UsageRecord(usage.Id,
            usage.SubscriptionId == 0 ? subscriptionId : usage.SubscriptionId,
            usage.ComponentHandle ?? componentHandle,
            usage.Quantity,
            usage.Memo,
            ParseTimestamp(usage.CreatedAt));
    }

    public async Task<UsageSummary?> GetUsageSummaryAsync(int subscriptionId, string componentHandle,
        CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionComponentEnvelope>(HttpMethod.Get,
            $"subscriptions/{subscriptionId}/components/handle:{Uri.EscapeDataString(componentHandle)}.json", null,
            cancellationToken, allowNotFound: true);

        var component = envelope?.Component;
        if (component is null)
        {
            return null;
        }

        return new UsageSummary(
            component.SubscriptionId == 0 ? subscriptionId : component.SubscriptionId,
            component.ComponentId,
            component.ComponentHandle ?? componentHandle,
            component.Name ?? componentHandle,
            component.UnitBalance ?? 0m);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        var currentPlanHandle = await GetCurrentPlanHandleAsync(subscriptionId, cancellationToken);

        // Applying at the next renewal is not a proration at all: nothing is charged or credited now,
        // and the new plan's price simply takes effect with the next period. The provider's migration
        // preview only models an immediate change, so the at-renewal preview is built from the target
        // plan's price (UC3, step 2).
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            var target = await GetPlanByHandleAsync(targetPlanHandle, cancellationToken)
                ?? throw new BillingProviderException(
                    $"Target plan '{targetPlanHandle}' does not resolve in product family '{_settings.ProductFamilyHandle}'.");

            return new PlanChangePreview(currentPlanHandle, targetPlanHandle, timing,
                proratedAdjustment: 0m, charge: target.Price, paymentDue: 0m, creditApplied: 0m);
        }

        var request = new MaxioMigrationRequest
        {
            Migration = new MaxioMigration { ProductHandle = targetPlanHandle, PreservePeriod = true }
        };

        var envelope = await SendAsync<MaxioMigrationPreviewEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/migrations/preview.json", request, cancellationToken);

        var migration = envelope?.Migration
            ?? throw new BillingProviderException(
                $"Maxio did not return a migration preview for subscription {subscriptionId}.");

        return new PlanChangePreview(currentPlanHandle, targetPlanHandle, timing,
            ToUnits(migration.ProratedAdjustmentInCents),
            ToUnits(migration.ChargeInCents),
            ToUnits(migration.PaymentDueInCents),
            ToUnits(migration.CreditAppliedInCents));
    }

    public async Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            // A delayed product change leaves the current period untouched and swaps the product at
            // the next renewal, so no proration is issued.
            var delayed = new MaxioUpdateSubscriptionRequest
            {
                Subscription = new MaxioUpdateSubscription
                {
                    ProductHandle = targetPlanHandle,
                    ProductChangeDelayed = true
                }
            };

            var updated = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Put,
                $"subscriptions/{subscriptionId}.json", delayed, cancellationToken);

            return RequireSubscription(updated,
                $"scheduling subscription {subscriptionId} to move to '{targetPlanHandle}' at renewal");
        }

        // preserve_period keeps the billing period and issues a prorated charge for the new plan.
        var request = new MaxioMigrationRequest
        {
            Migration = new MaxioMigration { ProductHandle = targetPlanHandle, PreservePeriod = true }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/migrations.json", request, cancellationToken);

        return RequireSubscription(envelope, $"moving subscription {subscriptionId} to '{targetPlanHandle}'");
    }

    public async Task<Subscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/hold.json", new { }, cancellationToken);

        return RequireSubscription(envelope, $"pausing subscription {subscriptionId}");
    }

    public async Task<Subscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/resume.json", null, cancellationToken);

        return RequireSubscription(envelope, $"resuming subscription {subscriptionId}");
    }

    public async Task<Subscription> CancelAsync(int subscriptionId, CancellationTiming timing, string? reason,
        CancellationToken cancellationToken = default)
    {
        var request = new MaxioCancellationRequest
        {
            Subscription = new MaxioCancellationOptions { CancellationMessage = reason }
        };

        if (timing == CancellationTiming.EndOfPeriod)
        {
            // The delayed-cancellation endpoint answers with a message rather than the subscription,
            // so the subscription is re-read to report the state and the effective date.
            await SendAsync<JsonElement?>(HttpMethod.Post, $"subscriptions/{subscriptionId}/delayed_cancel.json",
                request, cancellationToken);

            return await GetSubscriptionAsync(subscriptionId, cancellationToken)
                ?? throw new BillingProviderException(
                    $"Subscription {subscriptionId} could not be re-read after scheduling its cancellation.");
        }

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Delete,
            $"subscriptions/{subscriptionId}.json", request, cancellationToken);

        return RequireSubscription(envelope, $"cancelling subscription {subscriptionId}");
    }

    public async Task<Subscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Put,
            $"subscriptions/{subscriptionId}/reactivate.json", new { }, cancellationToken);

        return RequireSubscription(envelope, $"reactivating subscription {subscriptionId}");
    }

    /// <summary>
    /// UC2 precondition: refuse to record usage unless the configured handle resolves to a component
    /// of metered kind on the configured family. Checked once per process, then cached.
    /// </summary>
    private async Task EnsureMeteredComponentAsync(string componentHandle, CancellationToken cancellationToken)
    {
        if (_validationCache.IsValidated)
        {
            return;
        }

        var component = await GetComponentByHandleAsync(componentHandle, cancellationToken);
        if (component is null)
        {
            throw new BillingProviderException(
                $"Metered component '{componentHandle}' does not resolve on product family '{_settings.ProductFamilyHandle}'. Fix the seed (UC0) before reporting usage.");
        }

        if (!component.IsMetered)
        {
            throw new BillingProviderException(
                $"Component '{componentHandle}' is of kind '{component.Kind}', not metered, so usage cannot be recorded against it. A component's kind cannot be changed in place — archive it and recreate it as metered (UC0).");
        }

        _validationCache.MarkValidated();
    }

    private async Task<string> GetCurrentPlanHandleAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        var subscription = await GetSubscriptionAsync(subscriptionId, cancellationToken);

        return subscription?.PlanHandle ?? string.Empty;
    }

    /// <summary>
    /// Resolves the family's numeric id from its durable handle, because the component endpoints
    /// address the family by id. Cached for the lifetime of the client.
    /// </summary>
    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_productFamilyId.HasValue)
        {
            return _productFamilyId.Value;
        }

        var envelope = await SendAsync<MaxioProductFamilyEnvelope>(HttpMethod.Get,
            $"product_families/{FamilyReference()}.json", null, cancellationToken, allowNotFound: true);

        var id = envelope?.ProductFamily?.Id
            ?? throw new BillingProviderException(
                $"Product family '{_settings.ProductFamilyHandle}' does not resolve. Check the Maxio configuration against the seeded family (UC0).");

        _productFamilyId = id;

        return id;
    }

    /// <summary>
    /// Addresses the product family by handle when one is configured — ids are reassigned on a
    /// re-seed, handles are not — and falls back to the configured id otherwise.
    /// </summary>
    private string FamilyReference()
    {
        if (!string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            return $"handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle)}";
        }

        if (_settings.ProductFamilyId > 0)
        {
            return _settings.ProductFamilyId.ToString(CultureInfo.InvariantCulture);
        }

        throw new BillingProviderException(
            "Maxio is not configured: set either 'Maxio:ProductFamilyHandle' or 'Maxio:ProductFamilyId'.");
    }

    /// <summary>
    /// Issues the request and turns anything other than success into a
    /// <see cref="BillingProviderException"/> carrying the provider's own message.
    /// </summary>
    private async Task<T?> SendAsync<T>(HttpMethod method, string relativeUri, object? body,
        CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, relativeUri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: SerializerOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"The billing provider could not be reached: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException("The billing provider did not respond in time.", ex);
        }

        using (response)
        {
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var providerMessage = ExtractErrorMessage(payload);
                _logger.LogWarning("Maxio {0} {1} failed with {2}: {3}", method, relativeUri, (int)response.StatusCode,
                    providerMessage);

                throw new BillingProviderException(
                    $"The billing provider rejected the request ({(int)response.StatusCode}): {providerMessage}",
                    (int)response.StatusCode, providerMessage);
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(payload, SerializerOptions);
            }
            catch (JsonException ex)
            {
                throw new BillingProviderException(
                    $"The billing provider returned a response that could not be read: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Reads the provider's error wording out of either documented error shape —
    /// <c>{"errors": ["..."]}</c> or <c>{"errors": {"field": "..."}}</c>.
    /// </summary>
    private static string ExtractErrorMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return "no details were returned.";
        }

        try
        {
            var error = JsonSerializer.Deserialize<MaxioErrorResponse>(payload, SerializerOptions);
            if (error is not null)
            {
                if (!string.IsNullOrWhiteSpace(error.Error))
                {
                    return error.Error!;
                }

                switch (error.Errors.ValueKind)
                {
                    case JsonValueKind.Array:
                        var messages = error.Errors.EnumerateArray()
                            .Select(e => e.ToString())
                            .Where(m => !string.IsNullOrWhiteSpace(m))
                            .ToList();
                        if (messages.Count > 0)
                        {
                            return string.Join("; ", messages);
                        }
                        break;

                    case JsonValueKind.Object:
                        var fields = error.Errors.EnumerateObject()
                            .Select(p => $"{p.Name}: {p.Value}")
                            .ToList();
                        if (fields.Count > 0)
                        {
                            return string.Join("; ", fields);
                        }
                        break;

                    case JsonValueKind.String:
                        return error.Errors.GetString() ?? payload;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON, or not a shape we model — fall through and surface the raw payload.
        }

        return payload.Length > 500 ? payload[..500] : payload;
    }

    private static Subscription RequireSubscription(MaxioSubscriptionEnvelope? envelope, string operation)
    {
        if (envelope?.Subscription is null)
        {
            throw new BillingProviderException($"Maxio did not return a subscription when {operation}.");
        }

        return MapSubscription(envelope.Subscription);
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) =>
        new(product.Id,
            product.Handle ?? string.Empty,
            product.Name ?? string.Empty,
            product.Description,
            ToUnits(product.PriceInCents),
            product.Interval,
            product.IntervalUnit ?? string.Empty);

    private static BillingCustomer MapCustomer(MaxioCustomer customer) =>
        new(customer.Id,
            customer.Reference ?? string.Empty,
            customer.Email ?? string.Empty,
            customer.FirstName ?? string.Empty,
            customer.LastName ?? string.Empty);

    private static Subscription MapSubscription(MaxioSubscription subscription) =>
        new(subscription.Id,
            subscription.Customer?.Reference ?? string.Empty,
            subscription.Customer?.Id ?? 0,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            ToUnits(subscription.ProductPriceInCents),
            subscription.Product?.Interval ?? 0,
            subscription.Product?.IntervalUnit ?? string.Empty,
            ParseState(subscription.State),
            ParseTimestamp(subscription.CurrentPeriodEndsAt),
            ParseTimestamp(subscription.ActivatedAt),
            subscription.CancelAtEndOfPeriod ?? false,
            ParseTimestamp(subscription.DelayedCancelAt));

    private static MeteredComponent MapComponent(MaxioComponent component) =>
        new(component.Id,
            component.Handle ?? string.Empty,
            component.Name ?? string.Empty,
            component.Kind ?? string.Empty,
            component.PricingScheme,
            ParseDecimal(component.UnitPrice),
            component.Archived);

    /// <summary>Converts the provider's integer minor units to whole currency units.</summary>
    private static decimal ToUnits(long minorUnits) => minorUnits / (decimal)MinorUnitsPerUnit;

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Maps the provider's snake_case state onto the domain enum. An unrecognised value becomes
    /// <see cref="SubscriptionState.Unknown"/> rather than throwing, so a state added by the provider
    /// never breaks a read.
    /// </summary>
    private static SubscriptionState ParseState(string? state) => state switch
    {
        "pending" => SubscriptionState.Pending,
        "failed_to_create" => SubscriptionState.FailedToCreate,
        "trialing" => SubscriptionState.Trialing,
        "assessing" => SubscriptionState.Assessing,
        "active" => SubscriptionState.Active,
        "soft_failure" => SubscriptionState.SoftFailure,
        "past_due" => SubscriptionState.PastDue,
        "suspended" => SubscriptionState.Suspended,
        "canceled" => SubscriptionState.Canceled,
        "expired" => SubscriptionState.Expired,
        "paused" => SubscriptionState.Paused,
        "unpaid" => SubscriptionState.Unpaid,
        "trial_ended" => SubscriptionState.TrialEnded,
        "on_hold" => SubscriptionState.OnHold,
        "awaiting_signup" => SubscriptionState.AwaitingSignup,
        _ => SubscriptionState.Unknown
    };
}
