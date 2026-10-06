using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Talks to the Maxio Advanced Billing REST API over plain HTTPS.
///
/// Contract (verified against Maxio's official OpenAPI description and live sandbox):
/// - Base address: https://{subdomain}.chargify.com (US sites; overridable via Maxio:BaseUrl),
///   endpoints are top-level, e.g. GET /product_families/handle:{{handle}}/products.json.
/// - Auth: HTTP Basic; username = API key, password = "x".
/// - Customer/subscription uniqueness: "reference" values are site-unique; duplicates answer
///   HTTP 422 {"errors":["Reference: must be unique - that value has been taken."]}.
/// - Signup without a stored payment method: subscription "payment_collection_method":"remittance"
///   (invoice-based collection). Automatic collection would require a payment profile on file.
/// </summary>
public class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    // Maxio's documented signup shape for plans where no payment method is captured.
    private const string NO_CARD_PAYMENT_COLLECTION_METHOD = "remittance";

    private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioAdvancedBillingClient(HttpClient httpClient, MaxioSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public async Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(string familyHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var url = $"/product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json";
        using var response = await GetWithRetryAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new MaxioApiException((int)response.StatusCode, new[] { $"Product family '{familyHandle}' does not exist in this Maxio site." }, null);
        }

        await ThrowForFailureAsync(response, cancellationToken);

        var payload = JsonSerializer.Deserialize<MaxioWireModels.ProductEnvelope[]>(
            await ReadBodyAsync(response, cancellationToken), ReadOptions)
            ?? Array.Empty<MaxioWireModels.ProductEnvelope>();

        return payload
            .Where(envelope => envelope.Product is not null)
            .Select(envelope => ToPlan(envelope.Product!, familyHandle))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await GetWithRetryAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await ThrowForFailureAsync(response, cancellationToken);

        var envelope = JsonSerializer.Deserialize<MaxioWireModels.CustomerEnvelope>(
            await ReadBodyAsync(response, cancellationToken), ReadOptions);

        return envelope?.Customer is null ? null : ToCustomer(envelope.Customer);
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var createBody = new MaxioWireModels.CreateCustomerRequest(new MaxioWireModels.NewCustomer(
            firstName, lastName, email, reference));

        using var response = await _httpClient.PostAsync(
            "/customers.json",
            new StringContent(JsonSerializer.Serialize(createBody), Encoding.UTF8, "application/json"),
            cancellationToken);

        if (IsReferenceConflict(response, await ReadBodyAsync(response, cancellationToken)))
        {
            // A concurrent request created the same customer: adopt theirs, never create a second one.
            return await FindCustomerByReferenceAsync(reference, cancellationToken)
                ?? throw new MaxioApiException(500, new[] { $"Customer reference '{reference}' conflicted but the customer could not be retrieved." }, null);
        }

        await ThrowForFailureAsync(response, cancellationToken);

        var envelope = JsonSerializer.Deserialize<MaxioWireModels.CustomerEnvelope>(
            await ReadBodyAsync(response, cancellationToken), ReadOptions);

        return envelope?.Customer is null
            ? throw new MaxioApiException((int)response.StatusCode, null, "Maxio did not return the created customer.")
            : ToCustomer(envelope.Customer);
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var url = $"/subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await GetWithRetryAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await ThrowForFailureAsync(response, cancellationToken);

        var envelope = JsonSerializer.Deserialize<MaxioWireModels.SubscriptionEnvelope>(
            await ReadBodyAsync(response, cancellationToken), ReadOptions);

        return envelope?.Subscription is null ? null : ToSubscription(envelope.Subscription);
    }

    /// <summary>
    /// Returns the created subscription, or null when Maxio rejected the request because the
    /// unique reference is already taken (i.e. a duplicate enrollment request raced ahead).
    /// </summary>
    public async Task<MaxioSubscription?> SubscribeAsync(long customerId, string planHandle, string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var createBody = new MaxioWireModels.CreateSubscriptionRequest(new MaxioWireModels.NewSubscription(
            planHandle, customerId, reference, NO_CARD_PAYMENT_COLLECTION_METHOD));

        using var response = await _httpClient.PostAsync(
            "/subscriptions.json",
            new StringContent(JsonSerializer.Serialize(createBody), Encoding.UTF8, "application/json"),
            cancellationToken);

        var body = await ReadBodyAsync(response, cancellationToken);

        if (IsReferenceConflict(response, body))
        {
            return null;
        }

        await ThrowForFailureAsync(response, cancellationToken, body);

        var envelope = JsonSerializer.Deserialize<MaxioWireModels.SubscriptionEnvelope>(body, ReadOptions);

        return envelope?.Subscription is null ? null : ToSubscription(envelope.Subscription);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForCustomerAsync(long customerId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var url = $"/customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";
        using var response = await GetWithRetryAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<MaxioSubscription>();
        }

        await ThrowForFailureAsync(response, cancellationToken);

        var payload = JsonSerializer.Deserialize<MaxioWireModels.SubscriptionEnvelope[]>(
            await ReadBodyAsync(response, cancellationToken), ReadOptions)
            ?? Array.Empty<MaxioWireModels.SubscriptionEnvelope>();

        return payload
            .Where(envelope => envelope.Subscription is not null)
            .Select(envelope => ToSubscription(envelope.Subscription!))
            .ToList();
    }

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured())
        {
            throw new MaxioConfigurationException(
                "Maxio Advanced Billing is not configured. Provide Maxio:ApiKey and Maxio:Subdomain (or Maxio:BaseUrl) plus Maxio:ProductFamilyHandle via configuration - never in source control.");
        }
    }

    private async Task<HttpResponseMessage> GetWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        HttpResponseMessage? response = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            response?.Dispose();
            try
            {
                response = await _httpClient.GetAsync(url, cancellationToken);
                return response;
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException("unreachable");
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await response.Content.ReadAsStringAsync(
#if NET6_0_OR_GREATER
            cancellationToken
#endif
            );
    }

    private async Task ThrowForFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken, string? body = null)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        body ??= await ReadBodyAsync(response, cancellationToken);

        IReadOnlyList<string>? errors = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioWireModels.ErrorResponse>(body, ReadOptions);
            var collected = new List<string>();
            if (parsed?.Errors is not null) collected.AddRange(parsed.Errors);
            if (parsed?.Error is not null) collected.Add(parsed.Error);
            errors = collected;
        }
        catch (JsonException)
        {
            // Non-JSON error body: fall through with the raw text.
        }

        throw new MaxioApiException((int)response.StatusCode, errors, body);
    }

    /// <summary>
    /// True when Maxio answered a create with its documented unique-reference violation,
    /// which is the signal that a duplicate request already succeeded.
    /// </summary>
    private static bool IsReferenceConflict(HttpResponseMessage response, string body)
    {
        if ((int)response.StatusCode != 422 || string.IsNullOrEmpty(body))
        {
            return false;
        }

        return body.Contains("Reference", StringComparison.OrdinalIgnoreCase)
            && body.Contains("unique", StringComparison.OrdinalIgnoreCase);
    }

    private static MaxioPlan ToPlan(MaxioWireModels.ProductPayload product, string fallbackFamilyHandle)
    {
        return new MaxioPlan(
            ProductId: product.Id,
            Handle: product.Handle ?? string.Empty,
            Name: product.Name ?? product.Handle ?? string.Empty,
            Description: product.Description,
            PriceInCents: product.PriceInCents,
            Interval: product.Interval,
            IntervalUnit: product.IntervalUnit ?? string.Empty,
            RequiresPaymentMethod: product.RequireCreditCard,
            FamilyHandle: product.ProductFamily?.Handle ?? fallbackFamilyHandle,
            FamilyName: product.ProductFamily?.Name ?? string.Empty,
            ArchivedAt: product.ArchivedAt);
    }

    private static MaxioCustomer ToCustomer(MaxioWireModels.CustomerPayload customer)
    {
        return new MaxioCustomer(
            CustomerId: customer.Id,
            Reference: customer.Reference ?? string.Empty,
            FirstName: customer.FirstName ?? string.Empty,
            LastName: customer.LastName ?? string.Empty,
            Email: customer.Email ?? string.Empty);
    }

    private static MaxioSubscription ToSubscription(MaxioWireModels.SubscriptionPayload subscription)
    {
        return new MaxioSubscription(
            SubscriptionId: subscription.Id,
            Reference: subscription.Reference ?? string.Empty,
            State: subscription.State ?? string.Empty,
            CustomerId: subscription.CustomerId,
            ProductId: subscription.Product?.Id ?? subscription.ProductId,
            PlanHandle: subscription.Product?.Handle ?? string.Empty,
            PlanName: subscription.Product?.Name ?? string.Empty,
            PriceInCents: subscription.PriceInCents,
            Currency: subscription.Currency ?? string.Empty,
            NextBillingAt: subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            ActivatedAt: subscription.ActivatedAt,
            CreatedAt: subscription.CreatedAt,
            CanceledAt: subscription.CanceledAt,
            PaymentCollectionMethod: subscription.PaymentCollectionMethod ?? string.Empty);
    }
}
