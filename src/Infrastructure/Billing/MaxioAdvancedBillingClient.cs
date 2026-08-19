using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// HTTP client for Maxio Advanced Billing (Chargify). Authentication is HTTP Basic over TLS
/// with the API key as username and "X" as password.
/// </summary>
public class MaxioAdvancedBillingClient : IMaxioBillingGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var family = Uri.EscapeDataString(_options.ProductFamilyHandle.Trim());
        var path = $"product_families/handle:{family}/products.json?per_page=200";
        var envelopes = await GetJsonAsync<List<MaxioProductEnvelope>>(path, cancellationToken) ?? new List<MaxioProductEnvelope>();

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Handle) && p.ArchivedAt is null)
            .Select(p => new SubscriptionPlan
            {
                Id = p!.Id,
                Handle = p.Handle!,
                Name = p.Name ?? p.Handle!,
                Description = p.Description,
                Price = CentsToDecimal(p.PriceInCents),
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? "month"
            })
            .ToList();
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await GetJsonAsync<MaxioCustomerEnvelope>(path, cancellationToken, allowNotFound: true);
        return MapCustomer(envelope?.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        string reference,
        string email,
        string firstName,
        string lastName,
        string uniquenessToken,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var body = new CreateCustomerEnvelope
        {
            Customer = new CreateCustomerPayload
            {
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            },
            UniquenessToken = uniquenessToken
        };

        var envelope = await PostJsonAsync<CreateCustomerEnvelope, MaxioCustomerEnvelope>(
            "customers.json", body, cancellationToken);
        var customer = MapCustomer(envelope?.Customer);
        if (customer is null)
        {
            throw new BillingException("Maxio created a customer but returned an empty payload.");
        }

        return customer;
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var envelopes = await GetJsonAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken) ?? new List<MaxioSubscriptionEnvelope>();

        return envelopes
            .Select(e => MapSubscription(e.Subscription))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string uniquenessToken,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var body = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionPayload
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                // Remittance generates invoices without capturing a card, matching catalog
                // products that do not require a payment method at signup.
                PaymentCollectionMethod = "remittance"
            },
            UniquenessToken = uniquenessToken
        };

        var envelope = await PostJsonAsync<CreateSubscriptionEnvelope, MaxioSubscriptionEnvelope>(
            "subscriptions.json", body, cancellationToken);
        var subscription = MapSubscription(envelope?.Subscription);
        if (subscription is null)
        {
            throw new BillingException("Maxio created a subscription but returned an empty payload.");
        }

        return subscription;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new BillingException("Maxio:ApiKey is not configured.", 500);
        }

        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingException("Maxio:ProductFamilyHandle is not configured.", 500);
        }

        _options.ResolveBaseUrl();
    }

    private async Task<T?> GetJsonAsync<T>(string relativePath, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        using var response = await SendAsync(request, cancellationToken, retryOnServerError: true);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse?> PostJsonAsync<TRequest, TResponse>(
        string relativePath,
        TRequest body,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using var response = await SendAsync(request, cancellationToken, retryOnServerError: false);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadJsonAsync<TResponse>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        bool retryOnServerError)
    {
        HttpResponseMessage? response = null;
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            response?.Dispose();
            var attemptRequest = await CloneAsync(request, cancellationToken);
            response = await _httpClient.SendAsync(attemptRequest, cancellationToken);

            var status = (int)response.StatusCode;
            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests
                || (retryOnServerError && status >= 500 && status <= 599);
            if (!retryable || attempt == maxAttempts)
            {
                return response;
            }

            _logger.LogWarning(
                "Maxio request {Method} {Path} returned {StatusCode}; retrying ({Attempt}/{MaxAttempts}).",
                request.Method, request.RequestUri, status, attempt, maxAttempts);
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken);
        }

        return response!;
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = TryReadErrorMessage(payload)
            ?? $"Maxio request failed with HTTP {(int)response.StatusCode}.";

        if (response.StatusCode == HttpStatusCode.Conflict
            || message.Contains("DuplicatePrevention", StringComparison.OrdinalIgnoreCase)
            || message.Contains("reference", StringComparison.OrdinalIgnoreCase) && response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            throw new DuplicateException(message);
        }

        var statusCode = response.StatusCode switch
        {
            HttpStatusCode.UnprocessableEntity => 400,
            HttpStatusCode.BadRequest => 400,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Unauthorized => 502,
            HttpStatusCode.Forbidden => 502,
            HttpStatusCode.TooManyRequests => 503,
            _ => 502
        };

        throw new BillingException(message, statusCode);
    }

    private static string? TryReadErrorMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorResponse>(payload, JsonOptions);
            if (parsed?.Errors is { Count: > 0 })
            {
                return string.Join(" ", parsed.Errors);
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw payload.
        }

        return payload.Length > 500 ? payload[..500] : payload;
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private static BillingCustomer? MapCustomer(MaxioCustomer? customer)
    {
        if (customer is null || customer.Id == 0)
        {
            return null;
        }

        return new BillingCustomer
        {
            Id = customer.Id,
            Reference = customer.Reference,
            Email = customer.Email ?? string.Empty
        };
    }

    private static CustomerSubscription? MapSubscription(MaxioSubscription? subscription)
    {
        if (subscription is null || subscription.Id == 0)
        {
            return null;
        }

        return new CustomerSubscription
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            ProductHandle = subscription.Product?.Handle,
            ProductName = subscription.Product?.Name,
            Price = CentsToDecimal(subscription.ProductPriceInCents != 0
                ? subscription.ProductPriceInCents
                : subscription.Product?.PriceInCents ?? 0),
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
        };
    }

    private static decimal CentsToDecimal(long cents) => cents / 100m;

    internal static void ConfigureHttpClient(HttpClient client, MaxioOptions options)
    {
        var baseUrl = options.ResolveBaseUrl();
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(100);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }
}
