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
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single integration point with Maxio Advanced Billing, implementing <see cref="IBillingClient"/>
/// over a typed <see cref="HttpClient"/>. Nothing else in the application talks to Maxio.
///
/// Maxio authenticates with HTTP Basic where the username is the API key and the password is the
/// literal "x", and every resource is wrapped in a single named JSON property. The outbound base URL
/// comes from <see cref="MaxioSettings.ResolveBaseUrl"/>, so the same build targets production, a dev
/// tenant, or a local mock purely through configuration.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    /// <summary>Maxio's Basic-auth password is the literal "x"; the API key is the username.</summary>
    private const string ApiKeyPasswordPlaceholder = "x";

    /// <summary>Bill the subscription by invoice rather than collecting automatically from a card on file.</summary>
    private const string RemittanceCollectionMethod = "remittance";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // Maxio reports some numeric fields as strings (a usage quantity comes back as 1000 on
        // create but as "20.0" on list), so numbers must be readable from either form.
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        // The composition root normally sets BaseAddress from ResolveBaseUrl(); resolving it here too
        // keeps the client correct when it is constructed directly (tests, tooling).
        _httpClient.BaseAddress ??= new Uri(_settings.ResolveBaseUrl());

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new BillingConfigurationException(
                $"'{MaxioSettings.ConfigurationSection}:ApiKey' is not configured. Set it with dotnet user-secrets; it must never be committed.");
        }

        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_settings.ApiKey}:{ApiKeyPasswordPlaceholder}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyReference = _settings.ResolveProductFamilyReference();

        var products = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get,
            $"product_families/{familyReference}/products.json",
            body: null,
            cancellationToken);

        return products?
            .Select(envelope => envelope.Product)
            .Where(product => product is not null && product.ArchivedAt is null)
            .Select(product => ToPlan(product!))
            .ToList() ?? new List<SubscriptionPlan>();
    }

    public async Task<SubscriptionPlan?> GetPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        var envelope = await SendOrNullAsync<MaxioProductEnvelope>(HttpMethod.Get,
            $"products/handle/{Uri.EscapeDataString(planHandle)}.json",
            body: null,
            cancellationToken);

        return envelope?.Product is null ? null : ToPlan(envelope.Product);
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(string userReference, CancellationToken cancellationToken = default)
    {
        // Look the customer up by the eShopOnWeb user reference first, so repeating a subscribe never
        // creates a second provider-side customer for the same user.
        var existing = await SendOrNullAsync<MaxioCustomerEnvelope>(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(userReference)}",
            body: null,
            cancellationToken);

        if (existing?.Customer is not null)
        {
            return ToCustomer(existing.Customer);
        }

        var (firstName, lastName) = SplitName(userReference);
        var request = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = ToEmail(userReference),
                reference = userReference
            }
        };

        var created = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", request, cancellationToken);

        if (created?.Customer is null)
        {
            throw new BillingProviderException($"Maxio accepted the customer creation for '{userReference}' but returned no customer.");
        }

        return ToCustomer(created.Customer);
    }

    public async Task<IReadOnlyCollection<Subscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            body: null,
            cancellationToken);

        return subscriptions?
            .Select(envelope => envelope.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => ToSubscription(subscription!))
            .ToList() ?? new List<Subscription>();
    }

    public async Task<Subscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await SendOrNullAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get,
            $"subscriptions/{subscriptionId}.json",
            body: null,
            cancellationToken);

        return envelope?.Subscription is null ? null : ToSubscription(envelope.Subscription);
    }

    public async Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = planHandle,
                // The seeded plans do not require a payment method, so there is no card on file to
                // charge at signup. Collecting by remittance makes Maxio invoice the balance instead
                // of failing the enrollment on an automatic collection it cannot perform.
                payment_collection_method = RemittanceCollectionMethod
            }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);

        return RequireSubscription(envelope, $"enrolling customer {customerId} in plan '{planHandle}'");
    }

    public async Task<MeteredComponent?> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.MeteredComponentHandle))
        {
            throw new BillingConfigurationException(
                $"'{MaxioSettings.ConfigurationSection}:MeteredComponentHandle' is not configured, so pay-as-you-go usage cannot be recorded.");
        }

        var envelope = await SendOrNullAsync<MaxioComponentEnvelope>(HttpMethod.Get,
            $"components/lookup.json?handle={Uri.EscapeDataString(_settings.MeteredComponentHandle!.Trim())}",
            body: null,
            cancellationToken);

        return envelope?.Component is null ? null : ToMeteredComponent(envelope.Component);
    }

    public async Task<UsageRecord> RecordUsageAsync(int subscriptionId, int componentId, decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            usage = new
            {
                quantity,
                memo
            }
        };

        var envelope = await SendAsync<MaxioUsageEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/components/{componentId}/usages.json",
            request,
            cancellationToken);

        if (envelope?.Usage is null)
        {
            throw new BillingProviderException($"Maxio accepted the usage for subscription {subscriptionId} but returned no usage record.");
        }

        var usage = envelope.Usage;
        return new UsageRecord(usage.Id,
            usage.SubscriptionId == 0 ? subscriptionId : usage.SubscriptionId,
            usage.ComponentId == 0 ? componentId : usage.ComponentId,
            usage.ComponentHandle,
            usage.Quantity,
            usage.Memo,
            usage.CreatedAt);
    }

    public async Task<decimal> GetUsageTotalAsync(int subscriptionId, int componentId, CancellationToken cancellationToken = default)
    {
        // The component line item on the subscription carries the accrued balance for the period,
        // which is what the customer is told will appear on the next renewal invoice.
        var envelope = await SendAsync<MaxioSubscriptionComponentEnvelope>(HttpMethod.Get,
            $"subscriptions/{subscriptionId}/components/{componentId}.json",
            body: null,
            cancellationToken);

        return envelope?.Component?.UnitBalance ?? 0m;
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(Subscription subscription, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        var targetPlan = await GetPlanByHandleAsync(targetPlanHandle, cancellationToken);
        if (targetPlan is null)
        {
            throw new BillingConfigurationException(
                $"Plan '{targetPlanHandle}' does not resolve on the billing provider. Re-seed the sandbox (UC0) or correct the configured handles.");
        }

        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            // A delayed product change is never prorated: nothing is due now, and the new plan's full
            // price is assessed at the start of the next period.
            return new PlanChangePreview(subscription.Plan,
                targetPlan,
                timing,
                proratedAdjustmentInCents: 0,
                chargeInCents: targetPlan.PriceInCents,
                paymentDueInCents: 0,
                creditAppliedInCents: 0);
        }

        var request = new
        {
            migration = new
            {
                product_handle = targetPlanHandle,
                // Keep the current billing period and prorate, rather than restarting the period and
                // charging the new plan in full.
                preserve_period = true,
                include_coupons = true
            }
        };

        var envelope = await SendAsync<MaxioMigrationPreviewEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscription.Id}/migrations/preview.json",
            request,
            cancellationToken);

        var migration = envelope?.Migration
            ?? throw new BillingProviderException($"Maxio returned no migration preview for subscription {subscription.Id}.");

        return new PlanChangePreview(subscription.Plan,
            targetPlan,
            timing,
            migration.ProratedAdjustmentInCents,
            migration.ChargeInCents,
            migration.PaymentDueInCents,
            migration.CreditAppliedInCents);
    }

    public async Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            // A delayed product change schedules the new plan for the next renewal with no proration.
            var delayed = new
            {
                subscription = new
                {
                    product_handle = targetPlanHandle,
                    product_change_delayed = true
                }
            };

            var updated = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Put,
                $"subscriptions/{subscriptionId}.json",
                delayed,
                cancellationToken);

            return RequireSubscription(updated, $"scheduling subscription {subscriptionId} to move to '{targetPlanHandle}' at renewal");
        }

        var request = new
        {
            migration = new
            {
                product_handle = targetPlanHandle,
                preserve_period = true,
                include_coupons = true
            }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/migrations.json",
            request,
            cancellationToken);

        return RequireSubscription(envelope, $"migrating subscription {subscriptionId} to '{targetPlanHandle}'");
    }

    public async Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        // No automatic resume date: the customer resumes explicitly (UC4).
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/hold.json",
            new { hold = new { automatically_resume_at = (string?)null } },
            cancellationToken);

        return RequireSubscription(envelope, $"pausing subscription {subscriptionId}");
    }

    public async Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/resume.json",
            body: null,
            cancellationToken);

        return RequireSubscription(envelope, $"resuming subscription {subscriptionId}");
    }

    public async Task<Subscription> CancelSubscriptionAsync(int subscriptionId, CancellationTiming timing, string? reason, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            subscription = new
            {
                cancellation_message = reason
            }
        };

        if (timing == CancellationTiming.EndOfPeriod)
        {
            // Maxio answers a delayed cancel with only a confirmation message, so the resulting state
            // has to be read back from the subscription itself.
            await SendAsync<object>(HttpMethod.Post,
                $"subscriptions/{subscriptionId}/delayed_cancel.json",
                request,
                cancellationToken);

            return await GetSubscriptionAsync(subscriptionId, cancellationToken)
                ?? throw new BillingProviderException($"Maxio scheduled the cancellation of subscription {subscriptionId} but the subscription could no longer be read.");
        }

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Delete,
            $"subscriptions/{subscriptionId}.json",
            request,
            cancellationToken);

        return RequireSubscription(envelope, $"cancelling subscription {subscriptionId}");
    }

    public async Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        // Maxio's reactivate takes an optional, unwrapped options body; the defaults are what UC4 wants.
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Put,
            $"subscriptions/{subscriptionId}/reactivate.json",
            body: null,
            cancellationToken);

        return RequireSubscription(envelope, $"reactivating subscription {subscriptionId}");
    }

    /// <summary>Sends a request, mapping any non-success response onto a typed billing exception.</summary>
    private async Task<TResponse?> SendAsync<TResponse>(HttpMethod method, string requestUri, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, requestUri, body, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await ToProviderExceptionAsync(response, method, requestUri, cancellationToken);
        }

        return await ReadAsync<TResponse>(response, method, requestUri, cancellationToken);
    }

    /// <summary>
    /// As <see cref="SendAsync{TResponse}"/>, but a 404 means "no such entity" rather than a failure —
    /// used by the lookups that legitimately answer "not found" (customer by reference, plan by handle).
    /// </summary>
    private async Task<TResponse?> SendOrNullAsync<TResponse>(HttpMethod method, string requestUri, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, requestUri, body, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await ToProviderExceptionAsync(response, method, requestUri, cancellationToken);
        }

        return await ReadAsync<TResponse>(response, method, requestUri, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string requestUri, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: SerializerOptions);
        }

        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"The billing provider could not be reached for {method} {requestUri}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException($"The billing provider timed out for {method} {requestUri}.", ex);
        }
    }

    private static async Task<TResponse?> ReadAsync<TResponse>(HttpResponseMessage response, HttpMethod method, string requestUri, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
        {
            return default;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<TResponse>(payload, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new BillingProviderException($"The billing provider returned a response for {method} {requestUri} that could not be read: {ex.Message}", ex);
        }
    }

    private async Task<BillingProviderException> ToProviderExceptionAsync(HttpResponseMessage response, HttpMethod method, string requestUri, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ExtractErrors(payload);

        var detail = errors.Count > 0 ? string.Join("; ", errors) : Summarize(payload);
        var message = $"Maxio rejected {method} {requestUri} with {(int)response.StatusCode} {response.ReasonPhrase}"
            + (string.IsNullOrWhiteSpace(detail) ? "." : $": {detail}");

        _logger.LogWarning(message);

        return new BillingProviderException(message, (int)response.StatusCode, errors);
    }

    /// <summary>Pulls the provider's own messages out of an error payload so they can be shown to the actor.</summary>
    private static IReadOnlyCollection<string> ExtractErrors(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<string>();
        }

        try
        {
            var errorList = JsonSerializer.Deserialize<MaxioErrorList>(payload, SerializerOptions);
            if (errorList?.Errors is { Length: > 0 })
            {
                return errorList.Errors;
            }
        }
        catch (JsonException)
        {
            // Maxio also reports errors as a field-keyed object or as plain text; fall through and
            // surface the raw payload instead.
        }

        return Array.Empty<string>();
    }

    private static string Summarize(string payload)
    {
        const int maxLength = 500;
        var trimmed = payload.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static Subscription RequireSubscription(MaxioSubscriptionEnvelope? envelope, string operation)
    {
        if (envelope?.Subscription is null)
        {
            throw new BillingProviderException($"Maxio returned no subscription after {operation}.");
        }

        return ToSubscription(envelope.Subscription);
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product)
    {
        return new SubscriptionPlan(product.Id,
            product.Handle ?? string.Empty,
            product.Name ?? $"Product {product.Id}",
            product.Description,
            product.PriceInCents,
            product.Interval,
            product.IntervalUnit ?? string.Empty);
    }

    private static BillingCustomer ToCustomer(MaxioCustomer customer)
    {
        return new BillingCustomer(customer.Id,
            customer.Reference,
            customer.Email ?? string.Empty,
            customer.FirstName ?? string.Empty,
            customer.LastName ?? string.Empty);
    }

    private static Subscription ToSubscription(MaxioSubscription subscription)
    {
        var product = subscription.Product
            ?? throw new BillingProviderException($"Maxio returned subscription {subscription.Id} without its product, so the plan cannot be determined.");

        return new Subscription(subscription.Id,
            subscription.Customer?.Id ?? 0,
            subscription.Customer?.Reference,
            ToPlan(product),
            ToState(subscription.State),
            subscription.State ?? string.Empty,
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt,
            subscription.CancelAtEndOfPeriod ?? false,
            subscription.DelayedCancelAt,
            subscription.BalanceInCents,
            subscription.NextProductHandle);
    }

    private static MeteredComponent ToMeteredComponent(MaxioComponent component)
    {
        return new MeteredComponent(component.Id,
            component.Handle,
            component.Name ?? $"Component {component.Id}",
            component.Kind ?? string.Empty,
            component.PricingScheme,
            ParseUnitPrice(component.UnitPrice),
            component.ProductFamilyId);
    }

    /// <summary>
    /// Components price in decimal major units expressed as a string (e.g. "0.01" means one cent),
    /// unlike products and subscriptions which report integer minor units.
    /// </summary>
    private static decimal ParseUnitPrice(string? unitPrice)
    {
        return decimal.TryParse(unitPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;
    }

    private static SubscriptionState ToState(string? providerState)
    {
        return providerState switch
        {
            "active" or "trialing" => SubscriptionState.Active,
            "pending" or "assessing" or "awaiting_signup" => SubscriptionState.Pending,
            "past_due" or "soft_failure" or "unpaid" or "suspended" => SubscriptionState.PastDue,
            // `on_hold` is the result of pausing; `paused` is Maxio's own arrears state. Both stop
            // billing and both are resumable, so the storefront treats them the same.
            "on_hold" or "paused" => SubscriptionState.Paused,
            "canceled" => SubscriptionState.Cancelled,
            "expired" or "trial_ended" => SubscriptionState.Expired,
            _ => SubscriptionState.Unknown
        };
    }

    /// <summary>
    /// Maxio requires an email on a customer. eShopOnWeb usernames are email addresses, so the
    /// reference is used directly; anything else gets a deterministic local address.
    /// </summary>
    private static string ToEmail(string userReference)
    {
        return userReference.Contains('@', StringComparison.Ordinal)
            ? userReference
            : $"{userReference}@eshoponweb.local";
    }

    /// <summary>Derives the required first and last name from the eShopOnWeb user reference.</summary>
    private static (string FirstName, string LastName) SplitName(string userReference)
    {
        var localPart = userReference.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var firstName = parts.Length > 0 ? parts[0] : userReference;
        var lastName = parts.Length > 1 ? parts[^1] : "eShopOnWeb";

        return (firstName, lastName);
    }
}
