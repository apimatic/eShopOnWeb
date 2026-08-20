using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioBillingClient : IMaxioBillingClient
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient, ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListProductsInFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var familyKey = $"handle:{productFamilyHandle}";
        var path = $"product_families/{familyKey}/products.json?per_page=200";
        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken)
                        ?? new List<MaxioProductEnvelope>();

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => new SubscriptionPlan(
                p!.Handle!,
                p.Name ?? p.Handle!,
                p.Description,
                CentsToDollars(p.PriceInCents),
                p.Interval,
                p.IntervalUnit ?? "month"))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken, treatNotFoundAsNull: true);
        return MapCustomer(envelope?.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        CreateMaxioCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerBody
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Reference = request.Reference,
                Organization = request.Organization
            }
        };

        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken);
        var customer = MapCustomer(envelope?.Customer);
        if (customer is null)
        {
            throw new MaxioApiException("Maxio create-customer succeeded but returned no customer.", 200);
        }

        return customer;
    }

    public async Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get, path, null, cancellationToken, treatNotFoundAsNull: true);
        return MapSubscription(envelope?.Subscription);
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, path, null, cancellationToken)
                        ?? new List<MaxioSubscriptionEnvelope>();

        return envelopes
            .Select(e => MapSubscription(e.Subscription))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    public async Task<ShopperSubscription> CreateSubscriptionAsync(
        CreateMaxioSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionBody
            {
                ProductHandle = request.ProductHandle,
                CustomerId = request.CustomerId,
                Reference = request.Reference,
                PaymentCollectionMethod = "remittance"
            },
            UniquenessToken = request.UniquenessToken
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        var subscription = MapSubscription(envelope?.Subscription);
        if (subscription is null)
        {
            throw new MaxioApiException("Maxio create-subscription succeeded but returned no subscription.", 201);
        }

        return subscription;
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        bool treatNotFoundAsNull = false)
    {
        HttpResponseMessage? response = null;
        string? content = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativePath);
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            response = await _httpClient.SendAsync(request, cancellationToken);
            content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxAttempts)
            {
                _logger.LogWarning("Maxio returned 429 for {Method} {Path}; retrying in {Delay}.", method, relativePath, RetryDelay);
                await Task.Delay(RetryDelay, cancellationToken);
                continue;
            }

            break;
        }

        if (response is null)
        {
            throw new MaxioApiException("Maxio request failed before a response was received.", 0);
        }

        if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            var message = FormatError(response.StatusCode, relativePath, content);
            _logger.LogWarning("Maxio {Method} {Path} failed with {Status}.", method, relativePath, (int)response.StatusCode);
            throw new MaxioApiException(message, (int)response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(content, MaxioJson.SerializerOptions);
    }

    private static MaxioCustomer? MapCustomer(MaxioCustomerPayload? payload)
    {
        if (payload is null || payload.Id == 0)
        {
            return null;
        }

        return new MaxioCustomer(payload.Id, payload.Email ?? string.Empty, payload.Reference);
    }

    private static ShopperSubscription? MapSubscription(MaxioSubscription? payload)
    {
        if (payload is null || payload.Id == 0)
        {
            return null;
        }

        var nextBilling = payload.NextAssessmentAt ?? payload.CurrentPeriodEndsAt;
        var productHandle = payload.Product?.Handle ?? string.Empty;
        var productName = payload.Product?.Name ?? productHandle;
        var priceCents = payload.ProductPriceInCents != 0
            ? payload.ProductPriceInCents
            : payload.Product?.PriceInCents ?? 0;

        return new ShopperSubscription(
            payload.Id,
            payload.State ?? "unknown",
            productHandle,
            productName,
            CentsToDollars(priceCents),
            nextBilling);
    }

    private static decimal CentsToDollars(long cents) => cents / 100m;

    private static string FormatError(HttpStatusCode statusCode, string path, string? body)
    {
        var detail = ExtractErrorDetail(body);
        return string.IsNullOrWhiteSpace(detail)
            ? $"Maxio request to {path} failed with {(int)statusCode} {statusCode}."
            : $"Maxio request to {path} failed with {(int)statusCode} {statusCode}: {detail}";
    }

    private static string? ExtractErrorDetail(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return TrimForMessage(body);
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                var parts = errors.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                return string.Join("; ", parts);
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                var parts = errors.EnumerateObject()
                    .Select(p => $"{p.Name}: {p.Value}");
                return string.Join("; ", parts);
            }

            if (errors.ValueKind == JsonValueKind.String)
            {
                return errors.GetString();
            }
        }
        catch (JsonException)
        {
            return TrimForMessage(body);
        }

        return TrimForMessage(body);
    }

    private static string TrimForMessage(string body)
    {
        const int max = 500;
        var trimmed = body.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "...";
    }

    internal static AuthenticationHeaderValue CreateBasicAuthHeader(string apiKey)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:X"));
        return new AuthenticationHeaderValue("Basic", token);
    }
}
