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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The one class that talks to Maxio Advanced Billing, over plain HTTP (plan.md §2.2).
/// Requests are built from the Maxio OpenAPI specification in maxio-spec/openapi.yaml; results are
/// normalised into the provider-agnostic types in ApplicationCore and failures are surfaced as
/// <see cref="BillingProviderException"/>.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    /// <summary>
    /// Caches the product family id resolved from its handle. Maxio reassigns numeric ids whenever
    /// the catalog is re-created, so the handle is the durable identifier (plan.md §1.3).
    /// </summary>
    private int? _productFamilyId;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;

        // The composition root normally sets this from MaxioSettings.ResolveBaseUrl(); resolving it
        // here too keeps the client self-sufficient without ever overriding an explicit address.
        _httpClient.BaseAddress ??= new Uri(_settings.ResolveBaseUrl());

        // Maxio authenticates with HTTP Basic: the API key is the username, "x" is the password
        // (openapi.yaml securitySchemes.BasicAuth).
        if (_httpClient.DefaultRequestHeaders.Authorization is null && !string.IsNullOrEmpty(_settings.ApiKey))
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public async Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        var products = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get,
            $"product_families/{familyId}/products.json", null, nameof(ListPlansAsync), cancellationToken);

        return products?
            .Select(envelope => envelope.Product)
            .Where(product => product is not null && product.ArchivedAt is null)
            .Select(product => MapPlan(product!))
            .ToList() ?? new List<BillingPlan>();
    }

    public async Task<BillingPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(plan =>
            string.Equals(plan.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<MeteredComponent?> FindMeteredComponentAsync(string componentHandle,
        CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        var components = await SendAsync<List<MaxioComponentEnvelope>>(HttpMethod.Get,
            $"product_families/{familyId}/components.json", null, nameof(FindMeteredComponentAsync),
            cancellationToken);

        var match = components?
            .Select(envelope => envelope.Component)
            .FirstOrDefault(component => component is not null &&
                string.Equals(component.Handle, componentHandle, StringComparison.OrdinalIgnoreCase));

        return match is null ? null : MapComponent(match);
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(string customerReference, string email,
        string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        // Maxio enforces uniqueness on "reference", which is what makes this idempotent (plan.md §4.4).
        var existing = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(customerReference)}", null,
            nameof(EnsureCustomerAsync), cancellationToken, treatNotFoundAsNull: true);

        if (existing?.Customer is not null)
        {
            return MapCustomer(existing.Customer);
        }

        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = customerReference
            }
        };

        var created = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", request,
            nameof(EnsureCustomerAsync), cancellationToken);

        if (created?.Customer is null)
        {
            throw new BillingProviderException(nameof(EnsureCustomerAsync), 0,
                new[] { "The provider accepted the customer creation but returned no customer." });
        }

        return MapCustomer(created.Customer);
    }

    public async Task<IReadOnlyCollection<Subscription>> ListSubscriptionsAsync(string customerReference,
        CancellationToken cancellationToken = default)
    {
        var customer = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(customerReference)}", null,
            nameof(ListSubscriptionsAsync), cancellationToken, treatNotFoundAsNull: true);

        if (customer?.Customer is null)
        {
            return new List<Subscription>();
        }

        var subscriptions = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get,
            $"customers/{customer.Customer.Id}/subscriptions.json", null, nameof(ListSubscriptionsAsync),
            cancellationToken);

        return subscriptions?
            .Select(envelope => envelope.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => MapSubscription(subscription!))
            .ToList() ?? new List<Subscription>();
    }

    public async Task<Subscription?> GetSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get,
            $"subscriptions/{subscriptionId}.json", null, nameof(GetSubscriptionAsync), cancellationToken,
            treatNotFoundAsNull: true);

        return envelope?.Subscription is null ? null : MapSubscription(envelope.Subscription);
    }

    public async Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle,
        CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod)
                    ? null
                    : _settings.PaymentCollectionMethod
            }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request,
            nameof(CreateSubscriptionAsync), cancellationToken);

        return RequireSubscription(envelope, nameof(CreateSubscriptionAsync));
    }

    public async Task<UsageRecord> RecordUsageAsync(int subscriptionId, string componentHandle, decimal quantity,
        string? memo, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateUsageRequest
        {
            Usage = new MaxioCreateUsage { Quantity = quantity, Memo = memo }
        };

        var envelope = await SendAsync<MaxioUsageEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/components/{HandleRef(componentHandle)}/usages.json", request,
            nameof(RecordUsageAsync), cancellationToken);

        if (envelope?.Usage is null)
        {
            throw new BillingProviderException(nameof(RecordUsageAsync), 0,
                new[] { "The provider accepted the usage but returned no usage record." });
        }

        var usage = envelope.Usage;
        return new UsageRecord(usage.Id, usage.SubscriptionId == 0 ? subscriptionId : usage.SubscriptionId,
            usage.ComponentId, usage.ComponentHandle, usage.Quantity ?? quantity, usage.Memo, usage.CreatedAt);
    }

    public async Task<decimal?> GetUsageBalanceAsync(int subscriptionId, string componentHandle,
        CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionComponentEnvelope>(HttpMethod.Get,
            $"subscriptions/{subscriptionId}/components/{HandleRef(componentHandle)}.json", null,
            nameof(GetUsageBalanceAsync), cancellationToken, treatNotFoundAsNull: true);

        return envelope?.Component?.UnitBalance;
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            // A deferred product change raises no proration: nothing is owed now and the new plan
            // price simply takes effect next period (UC3 step 2).
            var target = await FindPlanAsync(targetPlanHandle, cancellationToken);
            if (target is null)
            {
                throw new BillingConfigurationException(
                    $"Plan handle '{targetPlanHandle}' does not resolve to a product.");
            }

            return new PlanChangePreview(targetPlanHandle, timing, 0, target.PriceInCents, 0, 0);
        }

        var request = new MaxioMigrationRequest
        {
            Migration = new MaxioMigration { ProductHandle = targetPlanHandle, PreservePeriod = true }
        };

        var envelope = await SendAsync<MaxioMigrationPreviewEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/migrations/preview.json", request, nameof(PreviewPlanChangeAsync),
            cancellationToken);

        if (envelope?.Migration is null)
        {
            throw new BillingProviderException(nameof(PreviewPlanChangeAsync), 0,
                new[] { "The provider returned no migration preview." });
        }

        var preview = envelope.Migration;
        return new PlanChangePreview(targetPlanHandle, timing, preview.ProratedAdjustmentInCents,
            preview.ChargeInCents, preview.PaymentDueInCents, preview.CreditAppliedInCents);
    }

    public async Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            var deferred = new MaxioUpdateSubscriptionRequest
            {
                Subscription = new MaxioUpdateSubscription
                {
                    ProductHandle = targetPlanHandle,
                    ProductChangeDelayed = true
                }
            };

            var updated = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Put,
                $"subscriptions/{subscriptionId}.json", deferred, nameof(ChangePlanAsync), cancellationToken);

            return RequireSubscription(updated, nameof(ChangePlanAsync));
        }

        var request = new MaxioMigrationRequest
        {
            Migration = new MaxioMigration { ProductHandle = targetPlanHandle, PreservePeriod = true }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/migrations.json", request, nameof(ChangePlanAsync), cancellationToken);

        return RequireSubscription(envelope, nameof(ChangePlanAsync));
    }

    public async Task<Subscription> PauseAsync(int subscriptionId, DateTimeOffset? automaticallyResumeAt,
        CancellationToken cancellationToken = default)
    {
        var request = new MaxioPauseRequest
        {
            Hold = new MaxioPauseOptions { AutomaticallyResumeAt = automaticallyResumeAt }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/hold.json", request, nameof(PauseAsync), cancellationToken);

        return RequireSubscription(envelope, nameof(PauseAsync));
    }

    public async Task<Subscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        // Resume takes no request body (openapi.yaml resumeSubscription).
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/resume.json", null, nameof(ResumeAsync), cancellationToken);

        return RequireSubscription(envelope, nameof(ResumeAsync));
    }

    public async Task<Subscription> CancelAsync(int subscriptionId, bool endOfPeriod, string? reason,
        CancellationToken cancellationToken = default)
    {
        var request = new MaxioCancellationRequest
        {
            Subscription = new MaxioCancellationOptions { CancellationMessage = reason }
        };

        if (endOfPeriod)
        {
            // A delayed cancellation only returns a confirmation message, so the subscription is
            // re-read to report the state and effective date the actor should see (UC4 step 4).
            await SendAsync<MaxioMessageResponse>(HttpMethod.Post,
                $"subscriptions/{subscriptionId}/delayed_cancel.json", request, nameof(CancelAsync),
                cancellationToken);

            var refreshed = await GetSubscriptionAsync(subscriptionId, cancellationToken);
            if (refreshed is null)
            {
                throw new BillingProviderException(nameof(CancelAsync), 0,
                    new[] { $"Subscription {subscriptionId} could not be re-read after the delayed cancellation." });
            }

            return refreshed;
        }

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Delete,
            $"subscriptions/{subscriptionId}.json", request, nameof(CancelAsync), cancellationToken);

        return RequireSubscription(envelope, nameof(CancelAsync));
    }

    public async Task<Subscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        // Reactivation is a PUT (openapi.yaml reactivateSubscription).
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Put,
            $"subscriptions/{subscriptionId}/reactivate.json", new MaxioReactivateRequest { Resume = true },
            nameof(ReactivateAsync), cancellationToken);

        return RequireSubscription(envelope, nameof(ReactivateAsync));
    }

    /// <summary>
    /// Resolves the configured product family handle to its current numeric id, caching the result
    /// for the lifetime of this client.
    /// </summary>
    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_productFamilyId is { } cached)
        {
            return cached;
        }

        var families = await SendAsync<List<MaxioProductFamilyEnvelope>>(HttpMethod.Get, "product_families.json",
            null, nameof(ResolveProductFamilyIdAsync), cancellationToken);

        var family = families?
            .Select(envelope => envelope.ProductFamily)
            .FirstOrDefault(candidate => candidate is not null &&
                string.Equals(candidate.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family is null)
        {
            throw new BillingConfigurationException(
                $"Product family handle '{_settings.ProductFamilyHandle}' was not found on site '{_settings.Subdomain}'.");
        }

        _productFamilyId = family.Id;
        return family.Id;
    }

    /// <summary>
    /// Issues a request and deserialises the response, translating any provider failure into a
    /// <see cref="BillingProviderException"/>.
    /// </summary>
    /// <param name="treatNotFoundAsNull">
    /// When true a 404 yields <c>null</c> instead of throwing — used for lookups where "absent" is
    /// a legitimate answer, such as an unknown customer reference or subscription id.
    /// </param>
    private async Task<TResponse?> SendAsync<TResponse>(HttpMethod method, string path, object? body,
        string operation, CancellationToken cancellationToken, bool treatNotFoundAsNull = false)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException(operation, "the provider could not be reached", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException(operation, "the provider did not respond in time", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound && treatNotFoundAsNull)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await BuildProviderExceptionAsync(operation, response, cancellationToken);
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<TResponse>(payload, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new BillingProviderException(operation, "the provider returned a response that could not be read", ex);
            }
        }
    }

    private static async Task<BillingProviderException> BuildProviderExceptionAsync(string operation,
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (status == (int)HttpStatusCode.Unauthorized)
        {
            return new BillingProviderException(operation, status,
                new[] { "The Maxio API key was rejected. Check the Maxio:ApiKey user-secret." });
        }

        IReadOnlyCollection<string> errors;
        try
        {
            errors = MaxioErrorReader.Flatten(
                JsonSerializer.Deserialize<MaxioErrorResponse>(payload, JsonOptions));
        }
        catch (JsonException)
        {
            // Some 404s return a bare string body rather than an error object.
            errors = string.IsNullOrWhiteSpace(payload) ? Array.Empty<string>() : new[] { payload.Trim() };
        }

        if (errors.Count == 0 && !string.IsNullOrWhiteSpace(payload))
        {
            errors = new[] { payload.Trim() };
        }

        return new BillingProviderException(operation, status, errors);
    }

    private static Subscription RequireSubscription(MaxioSubscriptionEnvelope? envelope, string operation)
    {
        if (envelope?.Subscription is null)
        {
            throw new BillingProviderException(operation, 0,
                new[] { "The provider returned no subscription." });
        }

        return MapSubscription(envelope.Subscription);
    }

    /// <summary>
    /// Addresses a component by handle rather than by numeric id, since Maxio reassigns ids when a
    /// catalog is re-created (openapi.yaml: "the component's handle prefixed by `handle:`").
    /// </summary>
    private static string HandleRef(string componentHandle) => $"handle:{componentHandle}";

    private static BillingPlan MapPlan(MaxioProduct product) => new(
        product.Id,
        product.Handle ?? string.Empty,
        product.Name ?? string.Empty,
        product.Description,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit ?? string.Empty,
        product.RequireCreditCard);

    private static MeteredComponent MapComponent(MaxioComponent component) => new(
        component.Id,
        component.Handle ?? string.Empty,
        component.Name ?? string.Empty,
        component.Kind ?? string.Empty,
        component.UnitName,
        component.PricingScheme,
        component.UnitPrice);

    private static BillingCustomer MapCustomer(MaxioCustomer customer) => new(
        customer.Id,
        customer.Reference ?? string.Empty,
        customer.Email ?? string.Empty,
        customer.FirstName ?? string.Empty,
        customer.LastName ?? string.Empty);

    private static Subscription MapSubscription(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.Customer?.Reference ?? string.Empty,
        subscription.Customer?.Id ?? 0,
        subscription.Product?.Handle ?? string.Empty,
        subscription.Product?.Name ?? string.Empty,
        subscription.Product?.PriceInCents ?? 0,
        MapState(subscription.State),
        subscription.CurrentPeriodEndsAt,
        subscription.ActivatedAt,
        subscription.CancelAtEndOfPeriod ?? false,
        subscription.DelayedCancelAt,
        subscription.AutomaticallyResumeAt);

    /// <summary>
    /// Maps the provider's state string onto the domain enum. Unrecognised values become
    /// <see cref="SubscriptionState.Unknown"/> rather than throwing, so a state added by the
    /// provider never breaks a read.
    /// </summary>
    private static SubscriptionState MapState(string? state) =>
        state?.ToLowerInvariant() switch
        {
            "pending" => SubscriptionState.Pending,
            "awaiting_signup" => SubscriptionState.AwaitingSignup,
            "failed_to_create" => SubscriptionState.FailedToCreate,
            "trialing" => SubscriptionState.Trialing,
            "trial_ended" => SubscriptionState.TrialEnded,
            "assessing" => SubscriptionState.Assessing,
            "active" => SubscriptionState.Active,
            "soft_failure" => SubscriptionState.SoftFailure,
            "past_due" => SubscriptionState.PastDue,
            "suspended" => SubscriptionState.Suspended,
            "paused" => SubscriptionState.Paused,
            "on_hold" => SubscriptionState.OnHold,
            "unpaid" => SubscriptionState.Unpaid,
            "canceled" => SubscriptionState.Canceled,
            "expired" => SubscriptionState.Expired,
            _ => SubscriptionState.Unknown
        };
}

internal class MaxioMessageResponse
{
    public string? Message { get; set; }
}
