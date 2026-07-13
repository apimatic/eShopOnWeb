using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Services;

// The single integration point with Maxio Advanced Billing (talks HTTP only, no SDK).
// Implements the provider-agnostic IBillingClient seam defined in ApplicationCore.
// BaseAddress/auth are configured by the composition root (see AddHttpClient<IBillingClient, ...>
// in ConfigureCoreServices / PublicApi Program.cs) so the target server stays config-driven (§2.3).
public class MaxioBillingClient : IBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get,
            $"product_families/handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle)}/products.json", null, cancellationToken);

        return products
            .Where(p => p.Product is not null)
            .Select(p => ToBillingPlan(p.Product!))
            .ToList();
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get,
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken);
            return envelope.Customer is null ? null : ToBillingCustomer(envelope.Customer);
        }
        catch (MaxioNotFoundException)
        {
            return null;
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateCustomerEnvelope
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken);
        return ToBillingCustomer(envelope.Customer!);
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json", null, cancellationToken);

        return subscriptions
            .Where(s => s.Subscription is not null)
            .Select(s => ToBillingSubscription(s.Subscription!))
            .ToList();
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateSubscriptionEnvelope
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId
            }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        return ToBillingSubscription(envelope.Subscription!);
    }

    public async Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get, $"subscriptions/{subscriptionId}.json", null, cancellationToken);
            return ToBillingSubscription(envelope.Subscription!);
        }
        catch (MaxioNotFoundException)
        {
            throw new SubscriptionNotFoundException(subscriptionId);
        }
    }

    public async Task<BillingComponent> GetComponentAsync(string componentHandle, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioComponentEnvelope>(HttpMethod.Get,
            $"components/lookup.json?handle={Uri.EscapeDataString(componentHandle)}", null, cancellationToken);

        if (envelope.Component is null)
        {
            throw new BillingProviderException($"Billing provider has no component with handle '{componentHandle}'.");
        }

        return ToBillingComponent(envelope.Component);
    }

    public async Task<int> GetComponentUnitBalanceAsync(int subscriptionId, string componentHandle, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionComponentEnvelope>(HttpMethod.Get,
            $"subscriptions/{subscriptionId}/components/handle:{Uri.EscapeDataString(componentHandle)}.json", null, cancellationToken);

        return envelope.Component?.UnitBalance ?? 0;
    }

    public async Task<BillingUsageRecord> RecordUsageAsync(int subscriptionId, string componentHandle, int quantity, string? memo, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateUsageEnvelope
        {
            Usage = new MaxioCreateUsage { Quantity = quantity, Memo = memo }
        };

        var envelope = await SendAsync<MaxioUsageEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/components/handle:{Uri.EscapeDataString(componentHandle)}/usages.json", body, cancellationToken);

        var usage = envelope.Usage!;
        return new BillingUsageRecord(usage.Id, usage.Quantity, usage.Memo);
    }

    public async Task<BillingPlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default)
    {
        var body = new MaxioMigrationEnvelope
        {
            Migration = new MaxioMigration { ProductHandle = targetProductHandle, PreservePeriod = true }
        };

        var envelope = await SendAsync<MaxioMigrationPreviewEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/migrations/preview.json", body, cancellationToken);

        var migration = envelope.Migration!;
        return new BillingPlanChangePreview(migration.ProratedAdjustmentInCents, migration.ChargeInCents, migration.PaymentDueInCents, migration.CreditAppliedInCents);
    }

    public async Task<BillingSubscription> ChangePlanNowAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default)
    {
        var body = new MaxioMigrationEnvelope
        {
            Migration = new MaxioMigration { ProductHandle = targetProductHandle, PreservePeriod = true }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post,
            $"subscriptions/{subscriptionId}/migrations.json", body, cancellationToken);

        return ToBillingSubscription(envelope.Subscription!);
    }

    public async Task<BillingSubscription> SchedulePlanChangeAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default)
    {
        var body = new MaxioDelayedProductChangeEnvelope
        {
            Subscription = new MaxioDelayedProductChange { ProductHandle = targetProductHandle, ProductChangeDelayed = true }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Put, $"subscriptions/{subscriptionId}.json", body, cancellationToken);
        return ToBillingSubscription(envelope.Subscription!);
    }

    public async Task<BillingSubscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var body = new MaxioPauseEnvelope();
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, $"subscriptions/{subscriptionId}/hold.json", body, cancellationToken);
        return ToBillingSubscription(envelope.Subscription!);
    }

    public async Task<BillingSubscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, $"subscriptions/{subscriptionId}/resume.json", null, cancellationToken);
        return ToBillingSubscription(envelope.Subscription!);
    }

    public async Task<BillingSubscription> CancelNowAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCancellationEnvelope
        {
            Subscription = new MaxioCancellationOptions { CancellationMessage = reason }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Delete, $"subscriptions/{subscriptionId}.json", body, cancellationToken);
        return ToBillingSubscription(envelope.Subscription!);
    }

    public async Task<BillingSubscription> CancelAtEndOfPeriodAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCancellationEnvelope
        {
            Subscription = new MaxioCancellationOptions { CancellationMessage = reason }
        };

        // This endpoint only echoes a confirmation message (Delayed-Cancellation-Response), not the
        // updated subscription — re-read the subscription afterward to get the current, authoritative state.
        await SendAsync<MaxioDelayedCancellationResponse>(HttpMethod.Post, $"subscriptions/{subscriptionId}/delayed_cancel.json", body, cancellationToken);
        return await GetSubscriptionAsync(subscriptionId, cancellationToken);
    }

    public async Task<BillingSubscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Put, $"subscriptions/{subscriptionId}/reactivate.json", new { }, cancellationToken);
        return ToBillingSubscription(envelope.Subscription!);
    }

    private static BillingPlan ToBillingPlan(MaxioProductWire p) =>
        new(p.Handle ?? string.Empty, p.Name, p.PriceInCents, p.Interval, p.IntervalUnit ?? "month");

    private static BillingCustomer ToBillingCustomer(MaxioCustomerWire c) =>
        new(c.Id, c.Reference ?? string.Empty);

    private static BillingSubscription ToBillingSubscription(MaxioSubscriptionWire s) => new(
        s.Id,
        s.State,
        s.Customer?.Id ?? 0,
        s.Customer?.Reference,
        s.Product?.Handle ?? string.Empty,
        s.Product?.Name ?? string.Empty,
        s.Product?.PriceInCents ?? 0,
        s.CurrentPeriodEndsAt,
        s.NextAssessmentAt,
        s.CancelAtEndOfPeriod ?? false);

    private static BillingComponent ToBillingComponent(MaxioComponentWire c) =>
        new(c.Id, c.Handle ?? string.Empty, c.Name, c.Kind);

    private async Task<TResponse> SendAsync<TResponse>(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Could not reach the billing provider ({method} {relativeUrl}): {ex.Message}", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new MaxioNotFoundException($"Billing provider returned 404 Not Found for {method} {relativeUrl}");
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new BillingProviderException(
                    $"Billing provider call {method} {relativeUrl} failed with status {(int)response.StatusCode}: {ExtractErrorMessage(errorBody)}");
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await JsonSerializer.DeserializeAsync<TResponse>(stream, JsonOptions, cancellationToken);
            return result ?? throw new BillingProviderException($"Billing provider returned an empty body for {method} {relativeUrl}");
        }
    }

    // Maxio's 422 error bodies are polymorphic across endpoints: {"errors": [...]}, {"error": "..."},
    // or (customers) {"errors": {...}}. Try each documented shape before falling back to the raw body.
    private static string ExtractErrorMessage(string errorBody)
    {
        if (string.IsNullOrWhiteSpace(errorBody))
        {
            return "(no response body)";
        }

        try
        {
            var arrayShape = JsonSerializer.Deserialize<MaxioErrorArrayResponse>(errorBody, JsonOptions);
            if (arrayShape?.Errors is { Count: > 0 })
            {
                return string.Join("; ", arrayShape.Errors);
            }
        }
        catch (JsonException)
        {
            // fall through to the next shape
        }

        try
        {
            var singleShape = JsonSerializer.Deserialize<MaxioSingleErrorResponse>(errorBody, JsonOptions);
            if (!string.IsNullOrWhiteSpace(singleShape?.Error))
            {
                return singleShape!.Error!;
            }
        }
        catch (JsonException)
        {
            // fall through to the raw body
        }

        return errorBody;
    }

    private sealed class MaxioNotFoundException : BillingProviderException
    {
        public MaxioNotFoundException(string message) : base(message)
        {
        }
    }

    private class MaxioDelayedCancellationResponse
    {
        public string? Message { get; set; }
    }
}
