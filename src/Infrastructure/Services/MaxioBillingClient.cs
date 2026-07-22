using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single integration point with Maxio Advanced Billing, implemented over a typed
/// <see cref="HttpClient"/>. Nothing else in the application talks to the provider.
/// </summary>
/// <remarks>
/// The outbound base address is resolved from <see cref="MaxioSettings.ResolveBaseUrl"/>, so an
/// explicit <c>Maxio:BaseUrl</c> always wins over the subdomain-derived host and the same build
/// can target production, a dev/sandbox tenant, or a local mock (§2.3). Maxio authenticates with
/// HTTP Basic where the username is the API key and the password is the literal "x".
/// </remarks>
public class MaxioBillingClient : IBillingClient
{
    private const string ApiKeyPassword = "x";

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

        // The composition root normally sets this; resolving it here too keeps the client
        // self-sufficient and means the configured target is honoured either way.
        _httpClient.BaseAddress ??= new Uri(_settings.ResolveBaseUrl());

        if (_httpClient.DefaultRequestHeaders.Authorization is null
            && !string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_settings.ApiKey}:{ApiKeyPassword}"));

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public string DefaultPlanHandle => _settings.DefaultProductHandle ?? string.Empty;

    public string MeteredComponentHandle => _settings.MeteredComponentHandle ?? string.Empty;

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(
        CancellationToken cancellationToken = default)
    {
        // Scope to the configured family by its durable handle; ids move when the sandbox is re-seeded.
        var path = string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle)
            ? "products.json"
            : $"product_families/handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle)}/products.json";

        using var document = await SendAsync(HttpMethod.Get, path, null, "list plans", cancellationToken);

        return ReadWrappedArray(document!.RootElement, "product")
            .Where(product => !HasValue(product, "archived_at"))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscriptionPlan?> GetPlanByHandleAsync(string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle)) return null;

        using var document = await SendAsync(HttpMethod.Get,
            $"products/handle/{Uri.EscapeDataString(planHandle)}.json", null,
            $"read plan '{planHandle}'", cancellationToken, allowNotFound: true);

        if (document is null) return null;

        return MapPlan(Unwrap(document.RootElement, "product"));
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(string userReference, string email,
        string? firstName, string? lastName, CancellationToken cancellationToken = default)
    {
        using (var existing = await SendAsync(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(userReference)}", null,
            $"look up customer '{userReference}'", cancellationToken, allowNotFound: true))
        {
            if (existing is not null)
            {
                return MapCustomer(Unwrap(existing.RootElement, "customer"));
            }
        }

        // Maxio requires a name, and eShopOnWeb identifies its users by email alone.
        var body = new JsonObject
        {
            ["customer"] = new JsonObject
            {
                ["reference"] = userReference,
                ["email"] = email,
                ["first_name"] = string.IsNullOrWhiteSpace(firstName) ? email : firstName,
                ["last_name"] = string.IsNullOrWhiteSpace(lastName) ? "eShopOnWeb" : lastName
            }
        };

        using var created = await SendAsync(HttpMethod.Post, "customers.json", body,
            $"create customer '{userReference}'", cancellationToken);

        return MapCustomer(Unwrap(created!.RootElement, "customer"));
    }

    public async Task<IReadOnlyCollection<Subscription>> ListSubscriptionsForCustomerAsync(
        int providerCustomerId, CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get,
            $"customers/{providerCustomerId}/subscriptions.json", null,
            $"list subscriptions for customer {providerCustomerId}", cancellationToken,
            allowNotFound: true);

        if (document is null) return Array.Empty<Subscription>();

        return ReadWrappedArray(document.RootElement, "subscription")
            .Select(MapSubscription)
            .ToList();
    }

    public async Task<Subscription?> GetSubscriptionAsync(int providerSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get,
            $"subscriptions/{providerSubscriptionId}.json", null,
            $"read subscription {providerSubscriptionId}", cancellationToken, allowNotFound: true);

        if (document is null) return null;

        return MapSubscription(Unwrap(document.RootElement, "subscription"));
    }

    public async Task<Subscription> CreateSubscriptionAsync(int providerCustomerId, string planHandle,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["subscription"] = new JsonObject
            {
                ["product_handle"] = planHandle,
                ["customer_id"] = providerCustomerId,
                // Invoice the customer rather than charging a card. The plans deliberately do
                // not require a payment method, and Maxio's default of "automatic" collection
                // would otherwise fail the signup for want of one.
                ["payment_collection_method"] = "remittance"
            }
        };

        using var document = await SendAsync(HttpMethod.Post, "subscriptions.json", body,
            $"subscribe customer {providerCustomerId} to plan '{planHandle}'", cancellationToken);

        return MapSubscription(Unwrap(document!.RootElement, "subscription"));
    }

    public async Task<MeteredComponent?> GetComponentByHandleAsync(string componentHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(componentHandle)) return null;

        using var document = await SendAsync(HttpMethod.Get,
            $"components/lookup.json?handle={Uri.EscapeDataString(componentHandle)}", null,
            $"read component '{componentHandle}'", cancellationToken, allowNotFound: true);

        if (document is null) return null;

        var component = Unwrap(document.RootElement, "component");

        // The component must live on the family the plans belong to, otherwise it is not
        // available to those subscriptions at all.
        var familyHandle = GetString(component, "product_family_handle");
        if (!string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle)
            && !string.IsNullOrWhiteSpace(familyHandle)
            && !string.Equals(familyHandle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Component '{0}' resolves on family '{1}' but '{2}' is configured.",
                componentHandle, familyHandle, _settings.ProductFamilyHandle);

            return null;
        }

        return MapComponent(component);
    }

    public async Task<UsageRecord> RecordUsageAsync(int providerSubscriptionId, string componentHandle,
        decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        var usage = new JsonObject { ["quantity"] = JsonValue.Create(quantity) };
        if (!string.IsNullOrWhiteSpace(memo))
        {
            usage["memo"] = memo;
        }

        var body = new JsonObject { ["usage"] = usage };

        using var document = await SendAsync(HttpMethod.Post,
            $"subscriptions/{providerSubscriptionId}/components/handle:{Uri.EscapeDataString(componentHandle)}/usages.json",
            body, $"record usage on subscription {providerSubscriptionId}", cancellationToken);

        return MapUsage(Unwrap(document!.RootElement, "usage"), providerSubscriptionId, componentHandle);
    }

    public async Task<decimal?> GetPeriodToDateUsageAsync(int providerSubscriptionId,
        string componentHandle, CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get,
            $"subscriptions/{providerSubscriptionId}/components/handle:{Uri.EscapeDataString(componentHandle)}.json",
            null, $"read usage balance on subscription {providerSubscriptionId}", cancellationToken,
            allowNotFound: true);

        if (document is null) return null;

        // Metered usage accumulates onto the component line item's unit balance.
        return GetDecimal(Unwrap(document.RootElement, "component"), "unit_balance");
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int providerSubscriptionId,
        string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        // A change deferred to the next renewal is not prorated, so nothing is charged now and
        // there is no migration to preview — the new plan's price simply starts next period.
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            return new PlanChangePreview(targetPlanHandle, timing, 0, 0, 0, 0);
        }

        var body = new JsonObject
        {
            ["migration"] = new JsonObject
            {
                ["product_handle"] = targetPlanHandle,
                // Keep the billing period and issue a prorated adjustment instead of rebilling in full.
                ["preserve_period"] = true
            }
        };

        using var document = await SendAsync(HttpMethod.Post,
            $"subscriptions/{providerSubscriptionId}/migrations/preview.json", body,
            $"preview plan change to '{targetPlanHandle}'", cancellationToken);

        var migration = Unwrap(document!.RootElement, "migration");

        return new PlanChangePreview(targetPlanHandle, timing,
            GetInt(migration, "prorated_adjustment_in_cents") ?? 0,
            GetInt(migration, "charge_in_cents") ?? 0,
            GetInt(migration, "payment_due_in_cents") ?? 0,
            GetInt(migration, "credit_applied_in_cents") ?? 0);
    }

    public async Task<Subscription> ChangePlanAsync(int providerSubscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            var delayed = new JsonObject
            {
                ["subscription"] = new JsonObject
                {
                    ["product_handle"] = targetPlanHandle,
                    ["product_change_delayed"] = true
                }
            };

            using var updated = await SendAsync(HttpMethod.Put,
                $"subscriptions/{providerSubscriptionId}.json", delayed,
                $"schedule plan change to '{targetPlanHandle}' at next renewal", cancellationToken);

            return MapSubscription(Unwrap(updated!.RootElement, "subscription"));
        }

        var body = new JsonObject
        {
            ["migration"] = new JsonObject
            {
                ["product_handle"] = targetPlanHandle,
                ["preserve_period"] = true
            }
        };

        using var document = await SendAsync(HttpMethod.Post,
            $"subscriptions/{providerSubscriptionId}/migrations.json", body,
            $"change plan to '{targetPlanHandle}'", cancellationToken);

        return MapSubscription(Unwrap(document!.RootElement, "subscription"));
    }

    public async Task<Subscription> PauseAsync(int providerSubscriptionId,
        DateTimeOffset? automaticallyResumeAt, CancellationToken cancellationToken = default)
    {
        JsonObject? body = null;
        if (automaticallyResumeAt.HasValue)
        {
            body = new JsonObject
            {
                ["hold"] = new JsonObject
                {
                    ["automatically_resume_at"] =
                        automaticallyResumeAt.Value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)
                }
            };
        }

        using var document = await SendAsync(HttpMethod.Post,
            $"subscriptions/{providerSubscriptionId}/hold.json", body,
            $"pause subscription {providerSubscriptionId}", cancellationToken);

        return MapSubscription(Unwrap(document!.RootElement, "subscription"));
    }

    public async Task<Subscription> ResumeAsync(int providerSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Post,
            $"subscriptions/{providerSubscriptionId}/resume.json", null,
            $"resume subscription {providerSubscriptionId}", cancellationToken);

        return MapSubscription(Unwrap(document!.RootElement, "subscription"));
    }

    public async Task<Subscription> CancelAsync(int providerSubscriptionId, CancellationTiming timing,
        string? reason, CancellationToken cancellationToken = default)
    {
        JsonObject? body = null;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            body = new JsonObject
            {
                ["subscription"] = new JsonObject { ["cancellation_message"] = reason }
            };
        }

        if (timing == CancellationTiming.EndOfPeriod)
        {
            // Delayed cancellation acknowledges with a message rather than the subscription,
            // so the caller's view is refreshed by re-reading it.
            using (await SendAsync(HttpMethod.Post,
                $"subscriptions/{providerSubscriptionId}/delayed_cancel.json", body,
                $"cancel subscription {providerSubscriptionId} at end of period", cancellationToken))
            {
            }

            var refreshed = await GetSubscriptionAsync(providerSubscriptionId, cancellationToken);

            return refreshed ?? throw new BillingProviderException(
                $"Subscription {providerSubscriptionId} could not be re-read after scheduling its cancellation.");
        }

        using var document = await SendAsync(HttpMethod.Delete,
            $"subscriptions/{providerSubscriptionId}.json", body,
            $"cancel subscription {providerSubscriptionId}", cancellationToken);

        return MapSubscription(Unwrap(document!.RootElement, "subscription"));
    }

    public async Task<Subscription> ReactivateAsync(int providerSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        // Reactivation takes an unwrapped body, unlike most Maxio operations.
        using var document = await SendAsync(HttpMethod.Put,
            $"subscriptions/{providerSubscriptionId}/reactivate.json", new JsonObject(),
            $"reactivate subscription {providerSubscriptionId}", cancellationToken);

        return MapSubscription(Unwrap(document!.RootElement, "subscription"));
    }

    /// <summary>
    /// Issues one request and returns the parsed body, translating every transport or provider
    /// failure into a <see cref="BillingProviderException"/>. Returns <c>null</c> only when the
    /// caller opted into treating 404 as "not found".
    /// </summary>
    private async Task<JsonDocument?> SendAsync(HttpMethod method, string path, JsonNode? body,
        string operation, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Accept", "application/json");

        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException(
                $"The billing provider could not be reached to {operation}.", ex);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new BillingProviderException($"The billing provider refused to {operation}",
                    (int)response.StatusCode, ExtractErrors(payload));
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                return JsonDocument.Parse("{}");
            }

            try
            {
                return JsonDocument.Parse(payload);
            }
            catch (JsonException ex)
            {
                throw new BillingProviderException(
                    $"The billing provider returned a response that could not be read when asked to {operation}.", ex);
            }
        }
    }

    /// <summary>
    /// Maxio reports errors as an array, a string, a map of strings, or a map of arrays, and
    /// sometimes as a single "error" property. All shapes are flattened to a list of messages.
    /// </summary>
    private static IReadOnlyCollection<string> ExtractErrors(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return Array.Empty<string>();

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<string>();
            }

            var messages = new List<string>();

            if (root.TryGetProperty("errors", out var errors))
            {
                CollectErrors(errors, messages);
            }

            if (root.TryGetProperty("error", out var single) && single.ValueKind == JsonValueKind.String)
            {
                var message = single.GetString();
                if (!string.IsNullOrWhiteSpace(message)) messages.Add(message);
            }

            return messages;
        }
        catch (JsonException)
        {
            return new[] { payload.Trim() };
        }
    }

    private static void CollectErrors(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text)) messages.Add(text);
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectErrors(item, messages);
                }
                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var before = messages.Count;
                    CollectErrors(property.Value, messages);

                    // Qualify the messages this property contributed with its field name.
                    for (var i = before; i < messages.Count; i++)
                    {
                        messages[i] = $"{property.Name}: {messages[i]}";
                    }
                }
                break;
        }
    }

    private SubscriptionPlan MapPlan(JsonElement product) =>
        new(GetInt(product, "id") ?? 0,
            GetString(product, "handle") ?? string.Empty,
            GetString(product, "name") ?? string.Empty,
            GetString(product, "description"),
            GetInt(product, "price_in_cents") ?? 0,
            GetInt(product, "interval") ?? 0,
            GetString(product, "interval_unit") ?? string.Empty,
            GetBool(product, "require_credit_card") ?? false);

    private static BillingCustomer MapCustomer(JsonElement customer) =>
        new(GetInt(customer, "id") ?? 0,
            GetString(customer, "reference") ?? string.Empty,
            GetString(customer, "email") ?? string.Empty,
            GetString(customer, "first_name"),
            GetString(customer, "last_name"));

    private Subscription MapSubscription(JsonElement subscription)
    {
        var plan = subscription.TryGetProperty("product", out var product)
            && product.ValueKind == JsonValueKind.Object
                ? MapPlan(product)
                : new SubscriptionPlan(0, string.Empty, string.Empty, null, 0, 0, string.Empty, false);

        var buyerId = subscription.TryGetProperty("customer", out var customer)
            && customer.ValueKind == JsonValueKind.Object
                ? GetString(customer, "reference") ?? string.Empty
                : string.Empty;

        var customerId = customer.ValueKind == JsonValueKind.Object
            ? GetInt(customer, "id") ?? 0
            : 0;

        return new Subscription(
            GetInt(subscription, "id") ?? 0,
            customerId,
            buyerId,
            plan,
            MapState(GetString(subscription, "state")),
            GetDateTimeOffset(subscription, "current_period_ends_at"),
            GetBool(subscription, "cancel_at_end_of_period") ?? false,
            GetDateTimeOffset(subscription, "canceled_at"),
            GetDateTimeOffset(subscription, "automatically_resume_at"));
    }

    private static MeteredComponent MapComponent(JsonElement component)
    {
        var kind = GetString(component, "kind");

        return new MeteredComponent(
            GetInt(component, "id") ?? 0,
            GetString(component, "handle") ?? string.Empty,
            GetString(component, "name") ?? string.Empty,
            string.Equals(kind, "metered_component", StringComparison.OrdinalIgnoreCase),
            GetString(component, "pricing_scheme"),
            GetDecimal(component, "unit_price"));
    }

    private static UsageRecord MapUsage(JsonElement usage, int subscriptionId, string componentHandle) =>
        new(GetLong(usage, "id") ?? 0,
            GetInt(usage, "subscription_id") ?? subscriptionId,
            GetInt(usage, "component_id") ?? 0,
            GetString(usage, "component_handle") ?? componentHandle,
            GetDecimal(usage, "quantity") ?? 0m,
            GetString(usage, "memo"),
            GetDateTimeOffset(usage, "created_at"));

    private static SubscriptionState MapState(string? state) => state switch
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
        // Maxio reports a held subscription as "on_hold"; "paused" is the legacy spelling.
        "paused" or "on_hold" => SubscriptionState.Paused,
        "unpaid" => SubscriptionState.Unpaid,
        "trial_ended" => SubscriptionState.TrialEnded,
        "awaiting_signup" => SubscriptionState.AwaitingSignup,
        _ => SubscriptionState.Unknown
    };

    /// <summary>
    /// Maxio list endpoints return an array of single-key wrapper objects
    /// (<c>[{"product": {...}}, ...]</c>) rather than a keyed collection.
    /// </summary>
    private static IEnumerable<JsonElement> ReadWrappedArray(JsonElement root, string wrapper)
    {
        if (root.ValueKind != JsonValueKind.Array) yield break;

        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty(wrapper, out var inner)
                && inner.ValueKind == JsonValueKind.Object)
            {
                yield return inner;
            }
        }
    }

    private static JsonElement Unwrap(JsonElement root, string wrapper)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(wrapper, out var inner)
            && inner.ValueKind == JsonValueKind.Object)
        {
            return inner;
        }

        throw new BillingProviderException(
            $"The billing provider returned a response without the expected '{wrapper}' object.");
    }

    private static bool HasValue(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }

    private static int? GetInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static long? GetLong(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static bool? GetBool(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    /// <summary>
    /// Reads a decimal that Maxio may express either as a number or as a string — component
    /// unit prices arrive as "0.01" and usage quantities as either 20 or "20.0".
    /// </summary>
    private static decimal? GetDecimal(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string property)
    {
        var text = GetString(element, property);
        if (string.IsNullOrWhiteSpace(text)) return null;

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
    }
}
