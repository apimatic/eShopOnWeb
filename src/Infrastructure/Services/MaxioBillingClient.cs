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
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The one class that talks to Maxio Advanced Billing (plan.md §2.2), implemented directly over a
/// typed <see cref="HttpClient"/> against the published OpenAPI contract. It translates Maxio's
/// vocabulary into the SubscriptionAggregate types and every failure into a
/// <see cref="BillingProviderException"/>, so nothing outside this file knows the provider exists.
/// </summary>
/// <remarks>
/// The outbound target is resolved by <see cref="MaxioSettings.ResolveBaseUrl"/> and applied to
/// <see cref="HttpClient.BaseAddress"/> in the composition roots, so prod / dev / mock is a
/// configuration change (plan.md §2.3).
/// </remarks>
public class MaxioBillingClient : IBillingClient
{
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

        // Maxio authenticates with HTTP Basic, api key as the username and a literal "x" as the
        // password (openapi.yaml: securitySchemes.BasicAuth).
        if (_httpClient.DefaultRequestHeaders.Authorization is null && !string.IsNullOrEmpty(_settings.ApiKey))
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string MeteredComponentHandle => _settings.MeteredComponentHandle;

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        // The product family path segment accepts "handle:<handle>", so the durable handle is used
        // rather than a numeric id that goes stale on a re-seed (plan.md §1.3).
        var envelopes = await SendAsync<List<ProductEnvelope>>(HttpMethod.Get,
            $"product_families/{FamilySegment()}/products.json", null, cancellationToken);

        return envelopes?
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => MapPlan(p!))
            .ToList() ?? new List<SubscriptionPlan>();
    }

    public async Task<SubscriptionPlan?> GetPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        var envelope = await SendOrNullAsync<ProductEnvelope>(HttpMethod.Get,
            $"products/handle/{Uri.EscapeDataString(planHandle)}.json", null, cancellationToken);

        return envelope?.Product is null ? null : MapPlan(envelope.Product);
    }

    public async Task<MeteredComponent?> GetComponentByHandleAsync(string componentHandle, CancellationToken cancellationToken = default)
    {
        var envelope = await SendOrNullAsync<ComponentEnvelope>(HttpMethod.Get,
            $"components/lookup.json?handle={Uri.EscapeDataString(componentHandle)}", null, cancellationToken);

        return envelope?.Component is null ? null : MapComponent(envelope.Component);
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var envelope = await SendOrNullAsync<CustomerEnvelope>(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(customerReference)}", null, cancellationToken);

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

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerBody
            {
                FirstName = string.IsNullOrWhiteSpace(firstName) ? customerReference : firstName,
                LastName = string.IsNullOrWhiteSpace(lastName) ? "Customer" : lastName,
                Email = email,
                Reference = customerReference
            }
        };

        var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Post, "customers.json", request, cancellationToken);

        if (envelope?.Customer is null)
        {
            throw new BillingProviderException($"Maxio did not return a customer when creating '{customerReference}'.");
        }

        return MapCustomer(envelope.Customer);
    }

    public async Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionBody
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod)
                    ? null
                    : _settings.PaymentCollectionMethod
            }
        };

        var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);

        return RequireSubscription(envelope, $"creating a subscription on '{planHandle}'");
    }

    public async Task<IReadOnlyCollection<Subscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var envelopes = await SendAsync<List<SubscriptionEnvelope>>(HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json", null, cancellationToken);

        return envelopes?
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList() ?? new List<Subscription>();
    }

    public async Task<Subscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await SendOrNullAsync<SubscriptionEnvelope>(HttpMethod.Get,
            $"subscriptions/{subscriptionId}.json", null, cancellationToken);

        return envelope?.Subscription is null ? null : MapSubscription(envelope.Subscription);
    }

    public async Task<UsageRecord> RecordUsageAsync(int subscriptionId,
        string componentHandle,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateUsageRequest
        {
            Usage = new CreateUsageBody { Quantity = quantity, Memo = memo }
        };

        var envelope = await SendAsync<UsageEnvelope>(HttpMethod.Post,
            UsagePath(subscriptionId, componentHandle), request, cancellationToken);

        if (envelope?.Usage is null)
        {
            throw new BillingProviderException(
                $"Maxio did not return a usage record for subscription {subscriptionId}.");
        }

        return MapUsage(envelope.Usage);
    }

    public async Task<IReadOnlyCollection<UsageRecord>> ListUsageAsync(int subscriptionId,
        string componentHandle,
        DateTimeOffset? since,
        CancellationToken cancellationToken = default)
    {
        var path = UsagePath(subscriptionId, componentHandle);
        if (since is not null)
        {
            // since_date filters from midnight on the given date; the caller trims to the exact
            // period start afterwards.
            path += $"?since_date={since.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        }

        var envelopes = await SendAsync<List<UsageEnvelope>>(HttpMethod.Get, path, null, cancellationToken);

        return envelopes?
            .Select(e => e.Usage)
            .Where(u => u is not null)
            .Select(u => MapUsage(u!))
            .ToList() ?? new List<UsageRecord>();
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        var current = await GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(subscriptionId);

        var envelope = await SendAsync<MigrationPreviewEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/migrations/preview.json",
            new MigrationRequest { Migration = BuildMigration(targetPlanHandle) },
            cancellationToken);

        var preview = envelope?.Migration
            ?? throw new BillingProviderException(
                $"Maxio did not return a migration preview for subscription {subscriptionId}.");

        return new PlanChangePreview(subscriptionId,
            current.PlanHandle,
            targetPlanHandle,
            PlanChangeTiming.Immediately,
            preview.ProratedAdjustmentInCents,
            preview.ChargeInCents,
            preview.PaymentDueInCents,
            preview.CreditAppliedInCents);
    }

    public async Task<Subscription> ChangePlanAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            // A delayed product change is scheduled on the subscription itself and is not prorated.
            var scheduled = await SendAsync<SubscriptionEnvelope>(HttpMethod.Put,
                $"subscriptions/{subscriptionId}.json",
                new UpdateSubscriptionRequest
                {
                    Subscription = new UpdateSubscriptionBody
                    {
                        ProductHandle = targetPlanHandle,
                        ProductChangeDelayed = true
                    }
                },
                cancellationToken);

            return RequireSubscription(scheduled, $"scheduling a change to '{targetPlanHandle}' at renewal");
        }

        // An immediate change is a migration, which preserves the period and prorates the charge.
        var migrated = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/migrations.json",
            new MigrationRequest { Migration = BuildMigration(targetPlanHandle) },
            cancellationToken);

        return RequireSubscription(migrated, $"migrating to '{targetPlanHandle}'");
    }

    public async Task<Subscription> PauseAsync(int subscriptionId, DateTimeOffset? automaticallyResumeAt, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/hold.json",
            new PauseRequest { Hold = new PauseBody { AutomaticallyResumeAt = automaticallyResumeAt } },
            cancellationToken);

        return RequireSubscription(envelope, $"pausing subscription {subscriptionId}");
    }

    public async Task<Subscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/resume.json", null, cancellationToken);

        return RequireSubscription(envelope, $"resuming subscription {subscriptionId}");
    }

    public async Task<Subscription> CancelAsync(int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var request = new CancellationRequest { Subscription = new CancellationBody { CancellationMessage = reason } };

        if (timing == CancellationTiming.EndOfPeriod)
        {
            // Delayed cancellation only acknowledges with a message, so the subscription is re-read
            // to report the state (and cancel_at_end_of_period flag) the provider now holds.
            await SendAsync<JsonElement>(HttpMethod.Post,
                $"subscriptions/{subscriptionId}/delayed_cancel.json", request, cancellationToken);

            return await GetSubscriptionAsync(subscriptionId, cancellationToken)
                ?? throw new SubscriptionNotFoundException(subscriptionId);
        }

        var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Delete,
            $"subscriptions/{subscriptionId}.json", request, cancellationToken);

        return RequireSubscription(envelope, $"cancelling subscription {subscriptionId}");
    }

    public async Task<Subscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Put,
            $"subscriptions/{subscriptionId}/reactivate.json", null, cancellationToken);

        return RequireSubscription(envelope, $"reactivating subscription {subscriptionId}");
    }

    private static MigrationBody BuildMigration(string targetPlanHandle) => new()
    {
        ProductHandle = targetPlanHandle,
        IncludeTrial = false,
        IncludeInitialCharge = false,
        IncludeCoupons = true,
        PreservePeriod = true
    };

    private string FamilySegment() =>
        string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle)
            ? _settings.ProductFamilyId.ToString(CultureInfo.InvariantCulture)
            : $"handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle)}";

    private static string UsagePath(int subscriptionId, string componentHandle) =>
        $"subscriptions/{subscriptionId}/components/handle:{Uri.EscapeDataString(componentHandle)}/usages.json";

    private static Subscription RequireSubscription(SubscriptionEnvelope? envelope, string operation) =>
        envelope?.Subscription is null
            ? throw new BillingProviderException($"Maxio did not return a subscription when {operation}.")
            : MapSubscription(envelope.Subscription);

    /// <summary>Sends a request, treating a 404 as "does not exist" rather than an error.</summary>
    private async Task<T?> SendOrNullAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync<T>(method, path, body, cancellationToken);
        }
        catch (BillingProviderException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return default;
        }
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: MaxioJson.Options);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Could not reach the billing provider at {_httpClient.BaseAddress}.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException($"The billing provider did not respond in time to {method} {path}.", ex);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw BuildFailure(method, path, response.StatusCode, payload);
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(payload, MaxioJson.Options);
            }
            catch (JsonException ex)
            {
                throw new BillingProviderException(
                    $"Could not read the billing provider's response to {method} {path}.", ex);
            }
        }
    }

    private BillingProviderException BuildFailure(HttpMethod method, string path, HttpStatusCode status, string payload)
    {
        var messages = ExtractErrors(payload);

        _logger.LogWarning("Maxio {0} {1} failed with {2}: {3}",
            method, path, (int)status, messages.Count > 0 ? string.Join("; ", messages) : payload);

        var summary = status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                "The billing provider rejected the configured credentials",
            HttpStatusCode.NotFound => $"The billing provider has no resource at {path}",
            _ => $"The billing provider rejected {method} {path}"
        };

        return new BillingProviderException(summary, (int)status, messages);
    }

    /// <summary>
    /// Maxio reports errors as a string array, a single string, or a field map depending on the
    /// endpoint (see components/schemas/errors) — all three are flattened to plain messages.
    /// </summary>
    private static IReadOnlyCollection<string> ExtractErrors(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<string>();
            }

            if (root.TryGetProperty("error", out var single) && single.ValueKind == JsonValueKind.String)
            {
                return new[] { single.GetString()! };
            }

            if (!root.TryGetProperty("errors", out var errors))
            {
                return Array.Empty<string>();
            }

            return errors.ValueKind switch
            {
                JsonValueKind.String => new[] { errors.GetString()! },
                JsonValueKind.Array => errors.EnumerateArray()
                    .Select(e => e.ToString())
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .ToList(),
                JsonValueKind.Object => errors.EnumerateObject()
                    .Select(p => $"{p.Name}: {FlattenValue(p.Value)}")
                    .ToList(),
                _ => Array.Empty<string>()
            };
        }
        catch (JsonException)
        {
            // A non-JSON error body (some 404s return a bare string) still carries information.
            return new[] { payload };
        }
    }

    private static string FlattenValue(JsonElement value) => value.ValueKind == JsonValueKind.Array
        ? string.Join(", ", value.EnumerateArray().Select(e => e.ToString()))
        : value.ToString();

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new(
        product.Id,
        product.Handle ?? string.Empty,
        product.Name ?? string.Empty,
        product.Description,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit ?? string.Empty,
        product.RequireCreditCard,
        product.ArchivedAt is not null,
        product.ProductFamily?.Handle);

    private static MeteredComponent MapComponent(MaxioComponent component) => new(
        component.Id,
        component.Handle ?? string.Empty,
        component.Name ?? string.Empty,
        MapComponentKind(component.Kind),
        component.PricingScheme,
        component.UnitPrice,
        component.UnitName,
        component.ProductFamilyId);

    private static BillingCustomer MapCustomer(MaxioCustomer customer) => new(
        customer.Id,
        // A customer created outside this integration may have no reference; the email is the
        // same identity value eShopOnWeb uses, so it is the natural fallback.
        string.IsNullOrWhiteSpace(customer.Reference) ? customer.Email ?? string.Empty : customer.Reference,
        customer.Email ?? string.Empty,
        customer.FirstName,
        customer.LastName);

    private static UsageRecord MapUsage(MaxioUsage usage) => new(
        usage.Id,
        usage.SubscriptionId,
        usage.ComponentId,
        usage.ComponentHandle,
        usage.Quantity,
        usage.Memo,
        usage.CreatedAt);

    private static Subscription MapSubscription(MaxioSubscription subscription)
    {
        var customer = subscription.Customer;
        var reference = string.IsNullOrWhiteSpace(customer?.Reference) ? customer?.Email : customer.Reference;

        return new Subscription(
            subscription.Id,
            customer?.Id ?? 0,
            string.IsNullOrWhiteSpace(reference) ? $"subscription-{subscription.Id}" : reference,
            MapState(subscription.State),
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            // The product carries the catalogue price; product_price_in_cents reflects what this
            // subscription is actually charged, so it wins when the two differ.
            subscription.ProductPriceInCents != 0 ? subscription.ProductPriceInCents : subscription.Product?.PriceInCents ?? 0,
            subscription.CurrentPeriodStartedAt,
            subscription.CurrentPeriodEndsAt,
            subscription.CancelAtEndOfPeriod ?? false,
            subscription.CanceledAt,
            subscription.NextProductHandle);
    }

    /// <summary>Maps Subscription-State.yaml onto the provider-agnostic enum.</summary>
    private static SubscriptionState MapState(string? state) => state switch
    {
        "pending" => SubscriptionState.Pending,
        "awaiting_signup" => SubscriptionState.AwaitingSignup,
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
        _ => SubscriptionState.Unknown
    };

    /// <summary>Maps Component-Kind.yaml onto the provider-agnostic enum.</summary>
    private static BillingComponentKind MapComponentKind(string? kind) => kind switch
    {
        "metered_component" => BillingComponentKind.Metered,
        "quantity_based_component" => BillingComponentKind.QuantityBased,
        "on_off_component" => BillingComponentKind.OnOff,
        "prepaid_usage_component" => BillingComponentKind.PrepaidUsage,
        "event_based_component" => BillingComponentKind.EventBased,
        _ => BillingComponentKind.Unknown
    };
}
