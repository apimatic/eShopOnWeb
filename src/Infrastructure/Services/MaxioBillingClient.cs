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

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The one and only place the application talks to Maxio Advanced Billing. Speaks the provider's
/// HTTP API directly over a typed <see cref="HttpClient"/>, normalizes its representations onto the
/// subscription domain types (integer cents become major units, provider states become
/// <see cref="SubscriptionState"/>), and turns provider failures into
/// <see cref="BillingProviderException"/>.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    /// <summary>Maxio authenticates with HTTP Basic where the API key is the user and the password is literally "x".</summary>
    private const string API_KEY_PASSWORD = "x";

    private const int CENTS_PER_UNIT = 100;

    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public MaxioBillingClient(HttpClient httpClient, MaxioSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = CreateBaseAddress(settings);
        }

        ConfigureAuthentication(_httpClient, settings);
    }

    /// <summary>
    /// Builds the outbound base address from configuration. An explicit <c>Maxio:BaseUrl</c> always
    /// wins over the subdomain-derived host, so the same build can target production, a dev tenant,
    /// or a local mock server. Composition roots call this rather than hardcoding a host.
    /// </summary>
    public static Uri CreateBaseAddress(MaxioSettings settings)
    {
        // A trailing slash keeps relative request paths from truncating a base URL that has a path.
        return new Uri(settings.ResolveBaseUrl().TrimEnd('/') + "/");
    }

    /// <summary>Applies the provider's Basic authentication scheme to a client.</summary>
    public static void ConfigureAuthentication(HttpClient httpClient, MaxioSettings settings)
    {
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{settings.ApiKey}:{API_KEY_PASSWORD}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        // Scoped to the configured family so unrelated products on the site are never offered.
        var path = string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle)
            ? "products.json"
            : $"product_families/handle:{Escape(_settings.ProductFamilyHandle)}/products.json";

        var products = await GetAsync<List<MaxioProductListItem>>(path, "list plans", cancellationToken);

        return products.Products()
            .Where(product => product.ArchivedAt is null)
            .Select(ToPlan)
            .ToList();
    }

    public async Task<SubscriptionPlan?> GetPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        var envelope = await GetOrNullAsync<MaxioProductEnvelope>(
            $"products/handle/{Escape(planHandle)}.json", "read plan", cancellationToken);

        return envelope?.Product is null ? null : ToPlan(envelope.Product);
    }

    public async Task<MeteredComponent?> GetComponentByHandleAsync(string componentHandle, CancellationToken cancellationToken = default)
    {
        var envelope = await GetOrNullAsync<MaxioComponentEnvelope>(
            $"components/lookup.json?handle={Escape(componentHandle)}", "read component", cancellationToken);

        return envelope?.Component is null ? null : ToComponent(envelope.Component);
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var envelope = await GetOrNullAsync<MaxioCustomerEnvelope>(
            $"customers/lookup.json?reference={Escape(customerReference)}", "look up customer", cancellationToken);

        return envelope?.Customer is null ? null : ToCustomer(envelope.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(string customerReference, string email, CancellationToken cancellationToken = default)
    {
        var (firstName, lastName) = SplitName(email);

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

        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", request,
            "create customer", cancellationToken);

        return envelope?.Customer is null
            ? throw new BillingProviderException("The billing provider returned no customer after creating one.")
            : ToCustomer(envelope.Customer);
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(string customerReference, string planHandle,
        CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = planHandle,
                CustomerReference = customerReference,

                // The demo plans require no payment method, so the subscription is invoiced rather
                // than charged at signup — otherwise the provider refuses for want of a card.
                PaymentCollectionMethod = string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod)
                    ? null
                    : _settings.PaymentCollectionMethod
            }
        };

        return await SendForSubscriptionAsync(HttpMethod.Post, "subscriptions.json", request, "create subscription",
            cancellationToken);
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsForCustomerAsync(int customerId,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await GetAsync<List<MaxioSubscriptionListItem>>(
            $"customers/{customerId}/subscriptions.json", "list customer subscriptions", cancellationToken);

        return subscriptions.Subscriptions().Select(ToSubscription).ToList();
    }

    public async Task<CustomerSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await GetOrNullAsync<MaxioSubscriptionEnvelope>(
            $"subscriptions/{subscriptionId}.json", "read subscription", cancellationToken);

        return envelope?.Subscription is null ? null : ToSubscription(envelope.Subscription);
    }

    public async Task<UsageRecord> RecordUsageAsync(int subscriptionId, string componentHandle, decimal quantity,
        string? memo, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateUsageRequest
        {
            Usage = new MaxioCreateUsage { Quantity = quantity, Memo = memo }
        };

        var envelope = await SendAsync<MaxioUsageEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/components/handle:{Escape(componentHandle)}/usages.json", request,
            "record usage", cancellationToken);

        return envelope?.Usage is null
            ? throw new BillingProviderException("The billing provider returned no usage record after recording usage.")
            : ToUsageRecord(envelope.Usage, subscriptionId);
    }

    public async Task<decimal?> GetUsageBalanceAsync(int subscriptionId, string componentHandle,
        CancellationToken cancellationToken = default)
    {
        var envelope = await GetOrNullAsync<MaxioSubscriptionComponentEnvelope>(
            $"subscriptions/{subscriptionId}/components/handle:{Escape(componentHandle)}.json",
            "read subscription component", cancellationToken);

        return envelope?.Component?.UnitBalance;
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioMigrationPreviewEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/migrations/preview.json", BuildMigrationRequest(targetPlanHandle),
            "preview plan change", cancellationToken);

        var migration = envelope?.Migration
            ?? throw new BillingProviderException("The billing provider returned no preview for the plan change.");

        return new PlanChangePreview(targetPlanHandle,
            ToMajorUnits(migration.ProratedAdjustmentInCents),
            ToMajorUnits(migration.ChargeInCents),
            ToMajorUnits(migration.PaymentDueInCents),
            ToMajorUnits(migration.CreditAppliedInCents));
    }

    public async Task<CustomerSubscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            // A delayed product change schedules the new plan for the next renewal, so nothing prorates.
            var delayed = new MaxioUpdateSubscriptionRequest
            {
                Subscription = new MaxioUpdateSubscription
                {
                    ProductHandle = targetPlanHandle,
                    ProductChangeDelayed = true
                }
            };

            return await SendForSubscriptionAsync(HttpMethod.Put, $"subscriptions/{subscriptionId}.json", delayed,
                "schedule plan change", cancellationToken);
        }

        return await SendForSubscriptionAsync(HttpMethod.Post, $"subscriptions/{subscriptionId}/migrations.json",
            BuildMigrationRequest(targetPlanHandle), "change plan", cancellationToken);
    }

    public async Task<CustomerSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        return await SendForSubscriptionAsync(HttpMethod.Post, $"subscriptions/{subscriptionId}/hold.json", null,
            "pause subscription", cancellationToken);
    }

    public async Task<CustomerSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        return await SendForSubscriptionAsync(HttpMethod.Post, $"subscriptions/{subscriptionId}/resume.json", null,
            "resume subscription", cancellationToken);
    }

    public async Task<CustomerSubscription> CancelSubscriptionAsync(int subscriptionId, CancellationTiming timing,
        string? reason, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCancellationRequest
        {
            Subscription = new MaxioCancellationOptions { CancellationMessage = reason }
        };

        if (timing == CancellationTiming.EndOfPeriod)
        {
            // A delayed cancellation answers with a message rather than the subscription, so the
            // provider's own view is re-read and returned as the outcome.
            await SendAsync<object>(HttpMethod.Post, $"subscriptions/{subscriptionId}/delayed_cancel.json", request,
                "cancel subscription at end of period", cancellationToken);

            return await GetSubscriptionAsync(subscriptionId, cancellationToken)
                ?? throw new BillingProviderException(
                    $"Subscription {subscriptionId} could not be re-read after scheduling its cancellation.");
        }

        return await SendForSubscriptionAsync(HttpMethod.Delete, $"subscriptions/{subscriptionId}.json", request,
            "cancel subscription", cancellationToken);
    }

    public async Task<CustomerSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        return await SendForSubscriptionAsync(HttpMethod.Put, $"subscriptions/{subscriptionId}/reactivate.json", null,
            "reactivate subscription", cancellationToken);
    }

    private static MaxioMigrationRequest BuildMigrationRequest(string targetPlanHandle)
    {
        return new MaxioMigrationRequest
        {
            Migration = new MaxioMigration
            {
                ProductHandle = targetPlanHandle,

                // Keeping the period is what makes the provider prorate the difference rather than
                // resetting the billing period and charging the new plan in full.
                PreservePeriod = true,
                IncludeTrial = false,
                IncludeInitialCharge = false
            }
        };
    }

    private async Task<CustomerSubscription> SendForSubscriptionAsync(HttpMethod method, string path, object? body,
        string operation, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(method, path, body, operation, cancellationToken);

        return envelope?.Subscription is null
            ? throw new BillingProviderException($"The billing provider returned no subscription for {operation}.")
            : ToSubscription(envelope.Subscription);
    }

    private async Task<T> GetAsync<T>(string path, string operation, CancellationToken cancellationToken) where T : new()
    {
        return await SendAsync<T>(HttpMethod.Get, path, null, operation, cancellationToken) ?? new T();
    }

    /// <summary>Reads a resource, answering null when the provider says it does not exist.</summary>
    private async Task<T?> GetOrNullAsync<T>(string path, string operation, CancellationToken cancellationToken)
    {
        using var response = await ExecuteAsync(HttpMethod.Get, path, null, operation, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        await EnsureSuccessAsync(response, operation, cancellationToken);

        return await ReadAsync<T>(response, operation, cancellationToken);
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, string operation,
        CancellationToken cancellationToken)
    {
        using var response = await ExecuteAsync(method, path, body, operation, cancellationToken);

        await EnsureSuccessAsync(response, operation, cancellationToken);

        return await ReadAsync<T>(response, operation, cancellationToken);
    }

    private async Task<HttpResponseMessage> ExecuteAsync(HttpMethod method, string path, object? body,
        string operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: _jsonOptions);
        }

        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new BillingProviderException($"The billing provider was unreachable while trying to {operation}.",
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException($"The billing provider timed out while trying to {operation}.",
                exception);
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, string operation,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
        {
            return default;
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new BillingProviderException(
                $"The billing provider returned a response that could not be read while trying to {operation}.",
                exception);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        throw new BillingProviderException(operation, (int)response.StatusCode, ExtractErrors(body));
    }

    /// <summary>
    /// Maxio reports failures either as a list of messages or as a field-to-message map; both are
    /// flattened so the caller sees the provider's own wording.
    /// </summary>
    private static IEnumerable<string> ExtractErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new[] { body };
            }

            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                return ReadErrorNode(errors, body);
            }

            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                return new[] { error.GetString() ?? body };
            }

            return new[] { body };
        }
        catch (JsonException)
        {
            return new[] { body };
        }
    }

    private static IEnumerable<string> ReadErrorNode(JsonElement errors, string body)
    {
        switch (errors.ValueKind)
        {
            case JsonValueKind.Array:
                return errors.EnumerateArray()
                    .Select(element => element.ToString())
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .ToList();
            case JsonValueKind.Object:
                return errors.EnumerateObject()
                    .Select(property => $"{property.Name}: {property.Value}")
                    .ToList();
            case JsonValueKind.String:
                return new[] { errors.GetString() ?? body };
            default:
                return new[] { body };
        }
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product)
    {
        return new SubscriptionPlan(product.Id,
            product.Handle ?? string.Empty,
            product.Name ?? string.Empty,
            ToMajorUnits(product.PriceInCents),
            product.Interval,
            product.IntervalUnit ?? string.Empty,
            product.RequireCreditCard);
    }

    private static MeteredComponent ToComponent(MaxioComponent component)
    {
        return new MeteredComponent(component.Id,
            component.Handle ?? string.Empty,
            component.Name ?? string.Empty,
            component.Kind ?? string.Empty,
            component.PricingScheme,
            ParseAmount(component.UnitPrice),
            component.ProductFamilyId);
    }

    private static BillingCustomer ToCustomer(MaxioCustomer customer)
    {
        return new BillingCustomer(customer.Id, customer.Reference, customer.Email ?? string.Empty);
    }

    private static CustomerSubscription ToSubscription(MaxioSubscription subscription)
    {
        return new CustomerSubscription(subscription.Id,
            ParseState(subscription.State),
            subscription.Customer?.Id ?? 0,
            subscription.Customer?.Reference,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,

            // The subscription carries the price actually subscribed to, which can differ from the
            // product's current price if the product was re-priced after signup.
            ToMajorUnits(subscription.ProductPriceInCents),
            subscription.CurrentPeriodEndsAt,
            subscription.CancelAtEndOfPeriod ?? false,
            subscription.DelayedCancelAt,
            subscription.NextProductHandle);
    }

    private static UsageRecord ToUsageRecord(MaxioUsage usage, int subscriptionId)
    {
        return new UsageRecord(usage.Id,
            usage.SubscriptionId == 0 ? subscriptionId : usage.SubscriptionId,
            usage.ComponentId,
            usage.ComponentHandle,
            ParseQuantity(usage.Quantity),
            usage.Memo,
            usage.CreatedAt);
    }

    /// <summary>The provider states every monetary amount in integer cents.</summary>
    private static decimal ToMajorUnits(long cents) => cents / (decimal)CENTS_PER_UNIT;

    private static decimal? ParseAmount(string? amount)
    {
        return decimal.TryParse(amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Usage quantities come back as either a JSON number or a JSON string.</summary>
    private static decimal ParseQuantity(JsonElement quantity)
    {
        return quantity.ValueKind switch
        {
            JsonValueKind.Number => quantity.GetDecimal(),
            JsonValueKind.String => ParseAmount(quantity.GetString()) ?? 0m,
            _ => 0m
        };
    }

    private static SubscriptionState ParseState(string? state)
    {
        return state switch
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

    /// <summary>
    /// The provider requires a first and last name; the eShopOnWeb user reference is an email, so a
    /// readable pair is derived from it rather than sending placeholder text.
    /// </summary>
    private static (string FirstName, string LastName) SplitName(string email)
    {
        var localPart = email.Split('@').FirstOrDefault() ?? email;
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

        return parts.Length > 1
            ? (Capitalize(parts[0]), Capitalize(parts[^1]))
            : (Capitalize(localPart), "eShopOnWeb");
    }

    private static string Capitalize(string value)
    {
        return string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value.Substring(1);
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
