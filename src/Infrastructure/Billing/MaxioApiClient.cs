using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Typed HTTP client for Maxio Advanced Billing (formerly Chargify).
/// Contract sourced from the official Advanced Billing API / SDK:
/// Basic auth (API key as username, "x" as password), JSON resources under https://{site}.chargify.com.
/// </summary>
public sealed class MaxioApiClient
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public MaxioApiClient(HttpClient http)
    {
        _http = http;
    }

    internal async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        var familyId = $"handle:{productFamilyHandle}";
        var path = $"product_families/{familyId}/products.json?per_page=200";
        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        if (envelopes is null)
        {
            throw new SubscriptionBillingException(
                $"Maxio product family '{productFamilyHandle}' was not found.", 404);
        }

        var products = new List<MaxioProduct>();
        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    internal async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return envelope?.Customer;
    }

    internal async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes attributes, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post,
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = attributes },
            cancellationToken);

        if (envelope?.Customer is null)
        {
            throw new SubscriptionBillingException("Maxio did not return a customer after create.");
        }

        return envelope.Customer;
    }

    internal async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken,
            allowNotFound: true);

        var subscriptions = new List<MaxioSubscription>();
        if (envelopes is null)
        {
            return subscriptions;
        }

        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    internal async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return envelope?.Subscription;
    }

    internal async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription payload, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = payload },
            cancellationToken);

        if (envelope?.Subscription is null)
        {
            throw new SubscriptionBillingException("Maxio did not return a subscription after create.");
        }

        return envelope.Subscription;
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SubscriptionBillingException("Timed out calling Maxio Advanced Billing.", 504);
        }
        catch (HttpRequestException ex)
        {
            throw new SubscriptionBillingException($"Unable to reach Maxio Advanced Billing: {ex.Message}", 502);
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
                throw MapError(response.StatusCode, payload);
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(payload, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new SubscriptionBillingException($"Unexpected Maxio response: {ex.Message}", 502);
            }
        }
    }

    internal static SubscriptionBillingException MapError(HttpStatusCode statusCode, string payload)
    {
        var detail = ExtractErrorDetail(payload);
        var code = (int)statusCode;

        if (code == 401 || code == 403)
        {
            return new SubscriptionBillingException(
                "Maxio Advanced Billing rejected the API credentials. Check Maxio:ApiKey and Maxio:Subdomain.",
                503);
        }

        if (code == 404)
        {
            return new SubscriptionBillingException(detail ?? "The requested Maxio resource was not found.", 404);
        }

        if (code == 422)
        {
            return new SubscriptionBillingException(detail ?? "Maxio rejected the billing request.", 400);
        }

        if (code >= 500)
        {
            return new SubscriptionBillingException(detail ?? "Maxio Advanced Billing is unavailable.", 502);
        }

        return new SubscriptionBillingException(detail ?? $"Maxio request failed with HTTP {code}.", 502);
    }

    internal static string? ExtractErrorDetail(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!root.TryGetProperty("errors", out var errors))
            {
                return Truncate(payload);
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var item in errors.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(item.GetString()!);
                    }
                    else
                    {
                        parts.Add(item.ToString());
                    }
                }

                return parts.Count > 0 ? string.Join(" ", parts) : Truncate(payload);
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                var parts = new List<string>();
                foreach (var property in errors.EnumerateObject())
                {
                    parts.Add($"{property.Name}: {property.Value}");
                }

                return parts.Count > 0 ? string.Join(" ", parts) : Truncate(payload);
            }

            if (errors.ValueKind == JsonValueKind.String)
            {
                return errors.GetString();
            }
        }
        catch (JsonException)
        {
            return Truncate(payload);
        }

        return Truncate(payload);
    }

    private static string Truncate(string value, int max = 500)
    {
        return value.Length <= max ? value : value[..max];
    }
}

internal sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamily
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class MaxioCreateCustomerRequest
{
    public MaxioCustomerAttributes Customer { get; set; } = new();
}

internal sealed class MaxioCustomerAttributes
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public int? ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

internal sealed class MaxioCreateSubscription
{
    public string? ProductHandle { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}
