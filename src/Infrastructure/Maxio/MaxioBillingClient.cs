using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var family = _options.ProductFamilyHandle.Trim();
        var path = $"/product_families/handle:{family}/products.json?include_archived=false&per_page=200";
        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken, retry: true)
                        ?? new List<MaxioProductEnvelope>();

        return envelopes
            .Where(e => e.Product is not null)
            .Select(e => e.Product!)
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => new BillingPlan(
                p.Handle!,
                p.Name ?? p.Handle!,
                p.Description,
                checked((int)p.PriceInCents),
                p.Interval,
                p.IntervalUnit ?? "month"))
            .ToList();
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var path = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Get, path, null, cancellationToken, retry: true, allowNotFound: true);
        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
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

        var payload = new MaxioCreateCustomerEnvelope
        {
            UniquenessToken = uniquenessToken,
            Customer = new MaxioCreateCustomerJson
            {
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            }
        };

        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post, "/customers.json", payload, cancellationToken, retry: false);
        if (envelope?.Customer is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio created a customer but returned no customer body.");
        }

        return MapCustomer(envelope.Customer);
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var path = $"/customers/{customerId}/subscriptions.json";
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get, path, null, cancellationToken, retry: true) ?? new List<MaxioSubscriptionEnvelope>();

        return envelopes
            .Where(e => e.Subscription is not null)
            .Select(e => MapSubscription(e.Subscription!))
            .ToList();
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string uniquenessToken,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var payload = new MaxioCreateSubscriptionEnvelope
        {
            UniquenessToken = uniquenessToken,
            Subscription = new MaxioCreateSubscriptionJson
            {
                CustomerId = customerId,
                ProductHandle = productHandle
            }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post, "/subscriptions.json", payload, cancellationToken, retry: false);
        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio created a subscription but returned no subscription body.");
        }

        return MapSubscription(envelope.Subscription);
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new MaxioNotConfiguredException();
        }
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        bool retry,
        bool allowNotFound = false)
    {
        const int maxAttempts = 3;
        HttpResponseMessage? response = null;
        string responseBody = string.Empty;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativePath);
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            response = await _httpClient.SendAsync(request, cancellationToken);
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (retry && attempt < maxAttempts && IsTransient(response.StatusCode))
            {
                _logger.LogWarning(
                    "Transient Maxio response {StatusCode} for {Method} {Path} (attempt {Attempt}). Retrying.",
                    (int)response.StatusCode, method, relativePath, attempt);
                response.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1)), cancellationToken);
                continue;
            }

            break;
        }

        using (response)
        {
            if (allowNotFound && response!.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (response!.StatusCode == HttpStatusCode.Conflict)
            {
                throw new MaxioDuplicateSubmissionException();
            }

            if (!response.IsSuccessStatusCode)
            {
                var message = ParseErrorMessage(responseBody);
                _logger.LogWarning(
                    "Maxio {Method} {Path} failed with {StatusCode}: {Message}",
                    method, relativePath, (int)response.StatusCode, message);
                throw new MaxioApiException(response.StatusCode, message);
            }

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests
           || (int)statusCode >= 500;

    private static string ParseErrorMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "Maxio request failed.";
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(errors.GetRawText()));
                reader.Read();
                return MaxioErrorsConverter.Flatten(ref reader);
            }
        }
        catch (JsonException)
        {
            // Fall through to a generic message; never echo raw bodies that might include secrets.
        }

        return "Maxio request failed.";
    }

    private static BillingCustomer MapCustomer(MaxioCustomerJson json)
        => new(json.Id, json.Reference, json.Email ?? string.Empty, json.FirstName ?? string.Empty, json.LastName ?? string.Empty);

    private static BillingSubscription MapSubscription(MaxioSubscriptionJson json)
        => new(
            json.Id,
            json.State ?? "unknown",
            json.Product?.Handle,
            json.Product?.Name,
            checked((int)json.ProductPriceInCents),
            json.CurrentPeriodEndsAt,
            json.NextAssessmentAt,
            json.Reference);
}
