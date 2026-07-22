using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
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
/// The single concrete billing client (the only class that talks HTTP to Maxio Advanced Billing),
/// implementing the provider-agnostic <see cref="IBillingClient"/> seam via a typed
/// <see cref="HttpClient"/>. It normalises provider results (cents → dollars) and surfaces failures
/// as <see cref="BillingProviderException"/> / <see cref="BillingConfigurationException"/>.
/// The outbound base URL is resolved from <see cref="MaxioSettings"/> in the composition root, so the
/// target server (prod / dev / mock) is configuration-driven (§2.3) — this class never hardcodes a host.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    // ---------------------------------------------------------------- Plans (UC1) ----

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = FamilySegment();
        var content = await SendAsync(HttpMethod.Get, $"product_families/{family}/products.json", null, cancellationToken);
        var products = Deserialize<List<ProductEnvelope>>(content, "list plans");

        return products
            .Where(p => p.Product is not null)
            .Select(p => MapPlan(p.Product!))
            .ToList();
    }

    // ---------------------------------------------------------------- Customers (§4.4) ----

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var encoded = Uri.EscapeDataString(reference);
        var content = await SendAsync(HttpMethod.Get, $"customers/lookup.json?reference={encoded}", null,
            cancellationToken, treatNotFoundAsNull: true);
        if (content is null)
        {
            return null;
        }

        var envelope = Deserialize<CustomerEnvelope>(content, "customer lookup");
        return MapCustomer(envelope.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(string reference, string email, CancellationToken cancellationToken = default)
    {
        var (firstName, lastName) = DeriveName(reference, email);
        var body = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            }
        };

        var content = await SendAsync(HttpMethod.Post, "customers.json", body, cancellationToken);
        var envelope = Deserialize<CustomerEnvelope>(content, "create customer");
        return MapCustomer(envelope.Customer);
    }

    // ---------------------------------------------------------------- Subscriptions (UC1) ----

    public async Task<CustomerSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId,
                // Invoice (remittance) billing so the demo enrols without card capture / 3-DS; the
                // charge accrues to an invoice rather than an automatic card collection at signup.
                payment_collection_method = "remittance"
            }
        };

        var content = await SendAsync(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        var envelope = Deserialize<SubscriptionEnvelope>(content, "create subscription");
        return MapSubscription(envelope.Subscription);
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var content = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", null, cancellationToken);
        var subscriptions = Deserialize<List<SubscriptionEnvelope>>(content, "list customer subscriptions");
        return subscriptions
            .Where(s => s.Subscription is not null)
            .Select(s => MapSubscription(s.Subscription))
            .ToList();
    }

    public async Task<CustomerSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var content = await SendAsync(HttpMethod.Get, $"subscriptions/{subscriptionId}.json", null, cancellationToken);
        var envelope = Deserialize<SubscriptionEnvelope>(content, "read subscription");
        return MapSubscription(envelope.Subscription);
    }

    // ---------------------------------------------------------------- Usage (UC2) ----

    public async Task<MeteredComponentInfo> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        var handle = Uri.EscapeDataString(_settings.MeteredComponentHandle);
        var content = await SendAsync(HttpMethod.Get, $"components/lookup.json?handle={handle}", null, cancellationToken);
        var envelope = Deserialize<ComponentEnvelope>(content, "metered component lookup");
        var component = envelope.Component
            ?? throw new BillingConfigurationException(
                $"Configured metered component handle '{_settings.MeteredComponentHandle}' did not resolve on the site (UC0).");

        return new MeteredComponentInfo(
            component.Id,
            component.Handle ?? _settings.MeteredComponentHandle,
            component.Kind ?? string.Empty,
            ParseMoney(component.UnitPrice));
    }

    public async Task<int> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default)
    {
        var componentSegment = ComponentSegment();
        var body = new
        {
            usage = new
            {
                quantity,
                memo
            }
        };

        var content = await SendAsync(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/components/{componentSegment}/usages.json", body, cancellationToken);

        // The response echoes the recorded usage; we return the quantity that was accepted.
        Deserialize<UsageEnvelope>(content, "record usage");
        return quantity;
    }

    public async Task<decimal?> GetUsageBalanceAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var componentSegment = ComponentSegment();
        var content = await SendAsync(HttpMethod.Get,
            $"subscriptions/{subscriptionId}/components/{componentSegment}.json", null, cancellationToken);
        var envelope = Deserialize<ComponentEnvelope>(content, "read subscription component");
        return envelope.Component?.UnitBalance;
    }

    // ---------------------------------------------------------------- Plan change (UC3) ----

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle,
        bool applyImmediately, CancellationToken cancellationToken = default)
    {
        if (applyImmediately)
        {
            // Immediate migration → prorated adjustment, preserving the current billing period.
            var body = new
            {
                migration = new
                {
                    product_handle = targetProductHandle,
                    preserve_period = true,
                    include_coupons = true
                }
            };

            var content = await SendAsync(HttpMethod.Post,
                $"subscriptions/{subscriptionId}/migrations/preview.json", body, cancellationToken);
            var envelope = Deserialize<MigrationPreviewEnvelope>(content, "preview plan change");
            var preview = envelope.Migration
                ?? throw new BillingProviderException("Maxio returned an empty migration preview.");

            return new PlanChangePreview(
                targetProductHandle,
                applyImmediately: true,
                proratedAdjustment: CentsToMoney(preview.ProratedAdjustmentInCents),
                chargeAmount: CentsToMoney(preview.ChargeInCents),
                paymentDue: CentsToMoney(preview.PaymentDueInCents),
                creditApplied: CentsToMoney(preview.CreditAppliedInCents));
        }

        // At-renewal change → no proration; the preview is simply the target plan's recurring price
        // effective from the next period.
        var plans = await ListPlansAsync(cancellationToken);
        var target = plans.FirstOrDefault(p => string.Equals(p.Handle, targetProductHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new BillingConfigurationException(
                $"Target plan handle '{targetProductHandle}' did not resolve on family '{_settings.ProductFamilyHandle}' (UC0).");

        return new PlanChangePreview(
            targetProductHandle,
            applyImmediately: false,
            proratedAdjustment: 0m,
            chargeAmount: target.Price,
            paymentDue: 0m,
            creditApplied: 0m);
    }

    public async Task<CustomerSubscription> ChangePlanAsync(int subscriptionId, string targetProductHandle,
        bool applyImmediately, CancellationToken cancellationToken = default)
    {
        if (applyImmediately)
        {
            var body = new
            {
                migration = new
                {
                    product_handle = targetProductHandle,
                    preserve_period = true,
                    include_coupons = true
                }
            };

            var content = await SendAsync(HttpMethod.Post,
                $"subscriptions/{subscriptionId}/migrations.json", body, cancellationToken);
            var envelope = Deserialize<SubscriptionEnvelope>(content, "commit plan change");
            return MapSubscription(envelope.Subscription);
        }

        // Delayed product change: schedule the new product for the next renewal.
        var delayedBody = new
        {
            subscription = new
            {
                product_handle = targetProductHandle,
                product_change_delayed = true
            }
        };

        var delayedContent = await SendAsync(HttpMethod.Put,
            $"subscriptions/{subscriptionId}.json", delayedBody, cancellationToken);
        var delayedEnvelope = Deserialize<SubscriptionEnvelope>(delayedContent, "schedule delayed plan change");
        return MapSubscription(delayedEnvelope.Subscription);
    }

    // ---------------------------------------------------------------- Lifecycle (UC4) ----

    public async Task<CustomerSubscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var content = await SendAsync(HttpMethod.Post, $"subscriptions/{subscriptionId}/hold.json", null, cancellationToken);
        var envelope = Deserialize<SubscriptionEnvelope>(content, "pause subscription");
        return MapSubscription(envelope.Subscription);
    }

    public async Task<CustomerSubscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var content = await SendAsync(HttpMethod.Post, $"subscriptions/{subscriptionId}/resume.json", null, cancellationToken);
        var envelope = Deserialize<SubscriptionEnvelope>(content, "resume subscription");
        return MapSubscription(envelope.Subscription);
    }

    public async Task<CustomerSubscription> CancelAsync(int subscriptionId, bool immediate, string? reason, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            subscription = new
            {
                cancellation_message = reason,
                reason_code = (string?)null
            }
        };

        if (immediate)
        {
            var content = await SendAsync(HttpMethod.Delete, $"subscriptions/{subscriptionId}.json", body, cancellationToken);
            var envelope = Deserialize<SubscriptionEnvelope>(content, "cancel subscription");
            return MapSubscription(envelope.Subscription);
        }

        // End-of-period cancellation returns only a message; re-read the subscription for its state.
        await SendAsync(HttpMethod.Post, $"subscriptions/{subscriptionId}/delayed_cancel.json", body, cancellationToken);
        return await GetSubscriptionAsync(subscriptionId, cancellationToken);
    }

    public async Task<CustomerSubscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var content = await SendAsync(HttpMethod.Put, $"subscriptions/{subscriptionId}/reactivate.json", null, cancellationToken);
        var envelope = Deserialize<SubscriptionEnvelope>(content, "reactivate subscription");
        return MapSubscription(envelope.Subscription);
    }

    // ---------------------------------------------------------------- HTTP plumbing ----

    private async Task<string?> SendAsync(HttpMethod method, string relativeUrl, object? body,
        CancellationToken cancellationToken, bool treatNotFoundAsNull = false)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Accept.ParseAdd("application/json");
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BillingProviderException($"Could not reach the billing provider: {ex.Message}", ex);
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound && treatNotFoundAsNull)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new BillingProviderException(BuildErrorMessage(response.StatusCode, content), (int)response.StatusCode);
        }

        return content;
    }

    private static T Deserialize<T>(string? content, string context)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new BillingProviderException($"Maxio returned an empty response for '{context}'.");
        }

        try
        {
            var result = JsonSerializer.Deserialize<T>(content, SerializerOptions);
            if (result is null)
            {
                throw new BillingProviderException($"Maxio returned a null response for '{context}'.");
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new BillingProviderException($"Could not parse the Maxio response for '{context}': {ex.Message}", ex);
        }
    }

    private static string BuildErrorMessage(HttpStatusCode statusCode, string? content)
    {
        var detail = ExtractProviderError(content);
        return detail is null
            ? $"Maxio returned {(int)statusCode} ({statusCode})."
            : $"Maxio returned {(int)statusCode} ({statusCode}): {detail}";
    }

    private static string? ExtractProviderError(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("errors", out var errors))
                {
                    return errors.ValueKind == JsonValueKind.Array
                        ? string.Join("; ", errors.EnumerateArray().Select(e => e.ToString()))
                        : errors.ToString();
                }

                if (root.TryGetProperty("error", out var error))
                {
                    return error.ToString();
                }
            }
        }
        catch (JsonException)
        {
            // fall through to the raw body
        }

        return content.Length > 500 ? content[..500] : content;
    }

    // ---------------------------------------------------------------- Mapping ----

    private static SubscriptionPlan MapPlan(ProductDto product) => new(
        product.Id,
        product.Handle ?? string.Empty,
        product.Name ?? string.Empty,
        product.Description,
        CentsToMoney(product.PriceInCents),
        product.IntervalUnit ?? "month",
        product.Interval <= 0 ? 1 : product.Interval,
        product.RequireCreditCard);

    private static BillingCustomer MapCustomer(CustomerDto? customer)
    {
        if (customer is null)
        {
            throw new BillingProviderException("Maxio returned a subscription/customer response without a customer.");
        }

        return new BillingCustomer(customer.Id, customer.Reference, customer.Email);
    }

    private static CustomerSubscription MapSubscription(SubscriptionDto? subscription)
    {
        if (subscription is null)
        {
            throw new BillingProviderException("Maxio returned an empty subscription response.");
        }

        return new CustomerSubscription(
            subscription.Id,
            subscription.State ?? string.Empty,
            subscription.Customer?.Id ?? 0,
            subscription.Customer?.Reference,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            CentsToMoney(subscription.Product?.PriceInCents ?? 0),
            subscription.Product?.IntervalUnit ?? "month",
            subscription.CurrentPeriodEndsAt,
            subscription.CancelAtEndOfPeriod ?? false,
            subscription.CanceledAt);
    }

    private static (string firstName, string lastName) DeriveName(string reference, string email)
    {
        var source = !string.IsNullOrWhiteSpace(email) ? email : reference;
        var local = source.Contains('@') ? source[..source.IndexOf('@')] : source;
        var firstName = string.IsNullOrWhiteSpace(local) ? "eShop" : local;
        return (firstName, "eShopOnWeb");
    }

    private static decimal CentsToMoney(long cents) => cents / 100m;

    private static decimal? ParseMoney(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private string FamilySegment()
        // Prefer the durable handle; numeric ids are reassigned on a sandbox re-seed (§1.3).
        => !string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle)
            ? $"handle:{_settings.ProductFamilyHandle}"
            : _settings.ProductFamilyId.ToString(CultureInfo.InvariantCulture);

    private string ComponentSegment()
        => $"handle:{_settings.MeteredComponentHandle}";

    // ---------------------------------------------------------------- Provider DTOs ----

    private sealed class CustomerEnvelope
    {
        public CustomerDto? Customer { get; set; }
    }

    private sealed class CustomerDto
    {
        public int Id { get; set; }
        public string? Reference { get; set; }
        public string? Email { get; set; }
    }

    private sealed class ProductEnvelope
    {
        public ProductDto? Product { get; set; }
    }

    private sealed class ProductDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Handle { get; set; }
        public string? Description { get; set; }
        public long PriceInCents { get; set; }
        public int Interval { get; set; }
        public string? IntervalUnit { get; set; }
        public bool RequireCreditCard { get; set; }
    }

    private sealed class SubscriptionEnvelope
    {
        public SubscriptionDto? Subscription { get; set; }
    }

    private sealed class SubscriptionDto
    {
        public int Id { get; set; }
        public string? State { get; set; }
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        public bool? CancelAtEndOfPeriod { get; set; }
        public DateTimeOffset? CanceledAt { get; set; }
        public CustomerDto? Customer { get; set; }
        public ProductDto? Product { get; set; }
    }

    private sealed class ComponentEnvelope
    {
        public ComponentDto? Component { get; set; }
    }

    private sealed class ComponentDto
    {
        public int Id { get; set; }
        public string? Handle { get; set; }
        public string? Kind { get; set; }
        public string? UnitPrice { get; set; }
        public int? UnitBalance { get; set; }
    }

    private sealed class UsageEnvelope
    {
        public JsonElement Usage { get; set; }
    }

    private sealed class MigrationPreviewEnvelope
    {
        public MigrationPreviewDto? Migration { get; set; }
    }

    private sealed class MigrationPreviewDto
    {
        public long ProratedAdjustmentInCents { get; set; }
        public long ChargeInCents { get; set; }
        public long PaymentDueInCents { get; set; }
        public long CreditAppliedInCents { get; set; }
    }
}
