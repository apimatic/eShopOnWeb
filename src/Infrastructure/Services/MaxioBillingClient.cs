using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single integration point with Maxio Advanced Billing (formerly Chargify). Implements
/// <see cref="IBillingClient"/> over a typed <see cref="HttpClient"/> whose BaseAddress is
/// resolved from <see cref="MaxioSettings"/> in the composition root (plan.md §2.2/§4.3).
/// Authenticated via HTTP Basic Auth: the site API key as username, the literal string "x" as
/// password (https://developers.maxio.com/http/getting-started/authentication).
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private const string MeteredComponentValidCacheKey = "Maxio:MeteredComponentValid";

    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;

    public MaxioBillingClient(HttpClient http, IOptions<MaxioSettings> options, IMemoryCache cache)
    {
        _http = http;
        _settings = options.Value;
        _cache = cache;

        var basicAuthValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuthValue);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await GetAsync<List<ProductEnvelope>>(
            $"/product_families/{_settings.ProductFamilyId}/products.json", cancellationToken);

        return products
            .Select(p => p.Product)
            .Where(p => p is { ArchivedAt: null })
            .Select(p => new SubscriptionPlan(p!.Handle!, p.Name!, p.PriceInCents, p.IntervalUnit!, p.Interval))
            .ToList();
    }

    public async Task<bool> IsMeteredComponentConfiguredCorrectlyAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(MeteredComponentValidCacheKey, out bool cached))
        {
            return cached;
        }

        bool isValid;
        try
        {
            var response = await GetAsync<ComponentEnvelope>(
                $"/product_families/{_settings.ProductFamilyId}/components/{_settings.MeteredComponentId}.json", cancellationToken);
            isValid = string.Equals(response.Component?.Kind, "metered_component", StringComparison.OrdinalIgnoreCase);
        }
        catch (BillingProviderException)
        {
            isValid = false;
        }

        // Cache a positive result for a while; re-check sooner if misconfigured so a fix is picked up quickly.
        _cache.Set(MeteredComponentValidCacheKey, isValid, TimeSpan.FromMinutes(isValid ? 10 : 1));
        return isValid;
    }

    public async Task<SubscriptionDetails?> FindActiveSubscriptionAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var subscriptions = await ListSubscriptionsAsync(customerReference, cancellationToken);
        return subscriptions.FirstOrDefault(s => s.IsActiveOrTrialing);
    }

    public async Task<SubscriptionDetails?> GetCurrentSubscriptionAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var subscriptions = await ListSubscriptionsAsync(customerReference, cancellationToken);
        // The provider returns a customer's subscriptions newest-first: the most recent one is "my
        // subscription" to manage by default, whatever its state - including Canceled, so Reactivate
        // can find it. Per-action legality (e.g. Resume only from OnHold) is enforced by SubscriptionService.
        return subscriptions.FirstOrDefault();
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await GetAsync<List<SubscriptionEnvelope>>(
            $"/customers/{customer.Id}/subscriptions.json", cancellationToken);

        return subscriptions.Select(s => Map(s.Subscription!)).ToList();
    }

    public async Task<SubscriptionDetails> CreateSubscriptionAsync(string customerReference, string customerEmail, string productHandle, CancellationToken cancellationToken = default)
    {
        var existingCustomer = await FindCustomerByReferenceAsync(customerReference, cancellationToken);

        var payload = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionPayload
            {
                ProductHandle = productHandle,
                PaymentCollectionMethod = "remittance",
                CustomerId = existingCustomer?.Id,
                CustomerReference = existingCustomer != null ? customerReference : null,
                CustomerAttributes = existingCustomer == null
                    ? new CustomerAttributesPayload
                    {
                        FirstName = customerEmail,
                        LastName = "(eShopOnWeb customer)",
                        Email = customerEmail,
                        Reference = customerReference
                    }
                    : null
            }
        };

        var response = await PostAsync<CreateSubscriptionEnvelope, SubscriptionEnvelope>("/subscriptions.json", payload, cancellationToken);
        return Map(response.Subscription!);
    }

    public async Task<SubscriptionDetails> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        using var httpResponse = await ExecuteAsync(() => _http.GetAsync($"/subscriptions/{subscriptionId}.json", cancellationToken));
        if (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new SubscriptionNotFoundException(subscriptionId);
        }

        await EnsureSuccessAsync(httpResponse, cancellationToken);
        var response = await httpResponse.Content.ReadFromJsonAsync<SubscriptionEnvelope>(cancellationToken: cancellationToken);
        return Map(response!.Subscription!);
    }

    public async Task<ComponentUsageStatus> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default)
    {
        var payload = new CreateUsageEnvelope
        {
            Usage = new CreateUsagePayload { Quantity = quantity, Memo = memo }
        };

        await PostAsync<CreateUsageEnvelope, UsageEnvelope>(
            $"/subscriptions/{subscriptionId}/components/{_settings.MeteredComponentId}/usages.json", payload, cancellationToken);

        try
        {
            return await GetComponentUsageStatusAsync(subscriptionId, cancellationToken);
        }
        catch (BillingProviderException)
        {
            // The usage report itself succeeded; only the read-back of the running total failed.
            // Report success with the total marked unavailable rather than failing the whole operation (UC2).
            return new ComponentUsageStatus(_settings.MeteredComponentHandle, isMetered: true, periodToDateUnitBalance: 0m, periodToDateUnavailable: true);
        }
    }

    public async Task<ComponentUsageStatus> GetComponentUsageStatusAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<ComponentEnvelope>(
            $"/subscriptions/{subscriptionId}/components/{_settings.MeteredComponentId}.json", cancellationToken);

        var component = response.Component!;
        return new ComponentUsageStatus(
            component.ComponentHandle ?? _settings.MeteredComponentHandle,
            string.Equals(component.Kind, "metered_component", StringComparison.OrdinalIgnoreCase),
            component.UnitBalance ?? 0m,
            periodToDateUnavailable: false);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken cancellationToken = default)
    {
        var current = await GetSubscriptionAsync(subscriptionId, cancellationToken);

        if (!applyImmediately)
        {
            // Delayed product changes apply at the next renewal with no proration
            // (https://developers.maxio.com/.../update-subscription): the "preview" is simply the target plan's price.
            var targetPlan = (await ListPlansAsync(cancellationToken)).FirstOrDefault(p => p.Handle == targetProductHandle);
            var chargeAtRenewal = targetPlan?.PriceInCents ?? 0m;
            return new PlanChangePreview(current.ProductHandle, targetProductHandle, applyImmediately: false, 0m, chargeAtRenewal, 0m, 0m);
        }

        var payload = new SubscriptionMigrationPreviewEnvelope
        {
            Migration = new SubscriptionMigrationPreviewPayload { ProductHandle = targetProductHandle }
        };

        var response = await PostAsync<SubscriptionMigrationPreviewEnvelope, MigrationPreviewEnvelope>(
            $"/subscriptions/{subscriptionId}/migrations/preview.json", payload, cancellationToken);

        var migration = response.Migration!;
        return new PlanChangePreview(
            current.ProductHandle,
            targetProductHandle,
            applyImmediately: true,
            migration.ProratedAdjustmentInCents,
            migration.ChargeInCents,
            migration.PaymentDueInCents,
            migration.CreditAppliedInCents);
    }

    public async Task<SubscriptionDetails> CommitPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken cancellationToken = default)
    {
        if (!applyImmediately)
        {
            var payload = new UpdateSubscriptionEnvelope
            {
                Subscription = new UpdateSubscriptionPayload
                {
                    ProductHandle = targetProductHandle,
                    ProductChangeDelayed = true
                }
            };

            var updateResponse = await PutAsync<UpdateSubscriptionEnvelope, SubscriptionEnvelope>(
                $"/subscriptions/{subscriptionId}.json", payload, cancellationToken);
            return Map(updateResponse.Subscription!);
        }

        var migrationPayload = new SubscriptionMigrationEnvelope
        {
            Migration = new SubscriptionMigrationPayload { ProductHandle = targetProductHandle }
        };

        var response = await PostAsync<SubscriptionMigrationEnvelope, SubscriptionEnvelope>(
            $"/subscriptions/{subscriptionId}/migrations.json", migrationPayload, cancellationToken);
        return Map(response.Subscription!);
    }

    public async Task<SubscriptionDetails> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync<object, SubscriptionEnvelope>($"/subscriptions/{subscriptionId}/hold.json", new { }, cancellationToken);
        return Map(response.Subscription!);
    }

    public async Task<SubscriptionDetails> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync<object, SubscriptionEnvelope>($"/subscriptions/{subscriptionId}/resume.json", new { }, cancellationToken);
        return Map(response.Subscription!);
    }

    public async Task<SubscriptionDetails> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        var payload = new CancellationEnvelope
        {
            Subscription = new CancellationPayload { CancellationMessage = reason }
        };

        if (endOfPeriod)
        {
            await PostAsync<CancellationEnvelope, DelayedCancellationEnvelope>(
                $"/subscriptions/{subscriptionId}/delayed_cancel.json", payload, cancellationToken);
        }
        else
        {
            await DeleteAsync($"/subscriptions/{subscriptionId}.json", payload, cancellationToken);
        }

        // Neither cancellation response carries the full updated subscription; re-read it as the
        // source of truth (mirrors the "re-read state before assuming outcome" guidance in UC3/UC4).
        return await GetSubscriptionAsync(subscriptionId, cancellationToken);
    }

    public async Task<SubscriptionDetails> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var response = await PutAsync<object, SubscriptionEnvelope>($"/subscriptions/{subscriptionId}/reactivate.json", new { }, cancellationToken);
        return Map(response.Subscription!);
    }

    private async Task<CustomerPayload?> FindCustomerByReferenceAsync(string customerReference, CancellationToken cancellationToken)
    {
        var uri = $"/customers/lookup.json?reference={Uri.EscapeDataString(customerReference)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await ExecuteAsync(() => _http.SendAsync(request, cancellationToken));

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Customer;
    }

    private static SubscriptionDetails Map(SubscriptionPayload subscription)
    {
        var product = subscription.Product ?? new ProductPayload();
        return new SubscriptionDetails(
            subscription.Id,
            subscription.Customer?.Reference ?? string.Empty,
            MapState(subscription.State),
            product.Handle ?? string.Empty,
            product.Name ?? string.Empty,
            product.PriceInCents,
            product.IntervalUnit ?? "month",
            product.Interval,
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt,
            subscription.CancelAtEndOfPeriod ?? false,
            subscription.DelayedCancelAt,
            subscription.OnHoldAt,
            subscription.AutomaticallyResumeAt);
    }

    private static SubscriptionState MapState(string? state) => state switch
    {
        "active" => SubscriptionState.Active,
        "trialing" => SubscriptionState.Trialing,
        "on_hold" => SubscriptionState.OnHold,
        "past_due" => SubscriptionState.PastDue,
        "unpaid" => SubscriptionState.Unpaid,
        "trial_ended" => SubscriptionState.TrialEnded,
        "canceled" => SubscriptionState.Canceled,
        _ => SubscriptionState.Unknown
    };

    private async Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        using var response = await ExecuteAsync(() => _http.GetAsync(uri, cancellationToken));
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        return result ?? throw new BillingProviderException($"Maxio returned an empty response for GET {uri}.");
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string uri, TRequest payload, CancellationToken cancellationToken)
    {
        using var response = await ExecuteAsync(() => _http.PostAsJsonAsync(uri, payload, cancellationToken));
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        return result ?? throw new BillingProviderException($"Maxio returned an empty response for POST {uri}.");
    }

    private async Task<TResponse> PutAsync<TRequest, TResponse>(string uri, TRequest payload, CancellationToken cancellationToken)
    {
        using var response = await ExecuteAsync(() => _http.PutAsJsonAsync(uri, payload, cancellationToken));
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        return result ?? throw new BillingProviderException($"Maxio returned an empty response for PUT {uri}.");
    }

    private async Task DeleteAsync<TRequest>(string uri, TRequest payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, uri)
        {
            Content = JsonContent.Create(payload)
        };
        using var response = await ExecuteAsync(() => _http.SendAsync(request, cancellationToken));
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Runs an outbound Maxio call, converting transport-level failures (unreachable host, DNS,
    /// TLS, timeout) into <see cref="BillingProviderException"/> so callers only ever need to
    /// handle one exception type for "the provider is unavailable" (UC1/UC2 failure scenarios).
    /// </summary>
    private static async Task<HttpResponseMessage> ExecuteAsync(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            return await send();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio is currently unreachable. Please try again shortly.", ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string message;
        try
        {
            var errors = await response.Content.ReadFromJsonAsync<ErrorsEnvelope>(cancellationToken: cancellationToken);
            message = errors?.Errors is { Count: > 0 }
                ? string.Join("; ", errors.Errors)
                : $"Maxio request failed with status {(int)response.StatusCode} ({response.StatusCode}).";
        }
        catch
        {
            message = $"Maxio request failed with status {(int)response.StatusCode} ({response.StatusCode}).";
        }

        throw new BillingProviderException(message);
    }

    // --- Wire-format DTOs (snake_case via JsonPropertyName; Maxio's envelope-per-resource shape) ---

    private class ErrorsEnvelope
    {
        [JsonPropertyName("errors")]
        public List<string>? Errors { get; set; }
    }

    private class ProductEnvelope
    {
        [JsonPropertyName("product")]
        public ProductPayload? Product { get; set; }
    }

    private class ProductPayload
    {
        [JsonPropertyName("handle")]
        public string? Handle { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("price_in_cents")]
        public decimal PriceInCents { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonPropertyName("interval_unit")]
        public string? IntervalUnit { get; set; }

        [JsonPropertyName("archived_at")]
        public DateTimeOffset? ArchivedAt { get; set; }
    }

    private class ComponentEnvelope
    {
        [JsonPropertyName("component")]
        public ComponentPayload? Component { get; set; }
    }

    private class ComponentPayload
    {
        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("component_handle")]
        public string? ComponentHandle { get; set; }

        [JsonPropertyName("unit_balance")]
        public decimal? UnitBalance { get; set; }
    }

    private class CustomerEnvelope
    {
        [JsonPropertyName("customer")]
        public CustomerPayload? Customer { get; set; }
    }

    private class CustomerPayload
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }
    }

    private class SubscriptionEnvelope
    {
        [JsonPropertyName("subscription")]
        public SubscriptionPayload? Subscription { get; set; }
    }

    private class SubscriptionPayload
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("product")]
        public ProductPayload? Product { get; set; }

        [JsonPropertyName("customer")]
        public CustomerPayload? Customer { get; set; }

        [JsonPropertyName("current_period_ends_at")]
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

        [JsonPropertyName("next_assessment_at")]
        public DateTimeOffset? NextAssessmentAt { get; set; }

        [JsonPropertyName("cancel_at_end_of_period")]
        public bool? CancelAtEndOfPeriod { get; set; }

        [JsonPropertyName("delayed_cancel_at")]
        public DateTimeOffset? DelayedCancelAt { get; set; }

        [JsonPropertyName("on_hold_at")]
        public DateTimeOffset? OnHoldAt { get; set; }

        [JsonPropertyName("automatically_resume_at")]
        public DateTimeOffset? AutomaticallyResumeAt { get; set; }
    }

    private class CreateSubscriptionEnvelope
    {
        [JsonPropertyName("subscription")]
        public CreateSubscriptionPayload? Subscription { get; set; }
    }

    private class CreateSubscriptionPayload
    {
        [JsonPropertyName("product_handle")]
        public string? ProductHandle { get; set; }

        [JsonPropertyName("payment_collection_method")]
        public string? PaymentCollectionMethod { get; set; }

        [JsonPropertyName("customer_id")]
        public int? CustomerId { get; set; }

        [JsonPropertyName("customer_reference")]
        public string? CustomerReference { get; set; }

        [JsonPropertyName("customer_attributes")]
        public CustomerAttributesPayload? CustomerAttributes { get; set; }
    }

    private class CustomerAttributesPayload
    {
        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }
    }

    private class CreateUsageEnvelope
    {
        [JsonPropertyName("usage")]
        public CreateUsagePayload? Usage { get; set; }
    }

    private class CreateUsagePayload
    {
        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("memo")]
        public string? Memo { get; set; }
    }

    private class UsageEnvelope
    {
        [JsonPropertyName("usage")]
        public object? Usage { get; set; }
    }

    private class SubscriptionMigrationEnvelope
    {
        [JsonPropertyName("migration")]
        public SubscriptionMigrationPayload? Migration { get; set; }
    }

    private class SubscriptionMigrationPayload
    {
        [JsonPropertyName("product_handle")]
        public string? ProductHandle { get; set; }

        [JsonPropertyName("include_coupons")]
        public bool IncludeCoupons { get; set; } = true;
    }

    private class SubscriptionMigrationPreviewEnvelope
    {
        [JsonPropertyName("migration")]
        public SubscriptionMigrationPreviewPayload? Migration { get; set; }
    }

    private class SubscriptionMigrationPreviewPayload
    {
        [JsonPropertyName("product_handle")]
        public string? ProductHandle { get; set; }

        [JsonPropertyName("include_coupons")]
        public bool IncludeCoupons { get; set; } = true;
    }

    private class MigrationPreviewEnvelope
    {
        [JsonPropertyName("migration")]
        public MigrationPreviewPayload? Migration { get; set; }
    }

    private class MigrationPreviewPayload
    {
        [JsonPropertyName("prorated_adjustment_in_cents")]
        public decimal ProratedAdjustmentInCents { get; set; }

        [JsonPropertyName("charge_in_cents")]
        public decimal ChargeInCents { get; set; }

        [JsonPropertyName("payment_due_in_cents")]
        public decimal PaymentDueInCents { get; set; }

        [JsonPropertyName("credit_applied_in_cents")]
        public decimal CreditAppliedInCents { get; set; }
    }

    private class UpdateSubscriptionEnvelope
    {
        [JsonPropertyName("subscription")]
        public UpdateSubscriptionPayload? Subscription { get; set; }
    }

    private class UpdateSubscriptionPayload
    {
        [JsonPropertyName("product_handle")]
        public string? ProductHandle { get; set; }

        [JsonPropertyName("product_change_delayed")]
        public bool ProductChangeDelayed { get; set; }
    }

    private class CancellationEnvelope
    {
        [JsonPropertyName("subscription")]
        public CancellationPayload? Subscription { get; set; }
    }

    private class CancellationPayload
    {
        [JsonPropertyName("cancellation_message")]
        public string? CancellationMessage { get; set; }
    }

    private class DelayedCancellationEnvelope
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
