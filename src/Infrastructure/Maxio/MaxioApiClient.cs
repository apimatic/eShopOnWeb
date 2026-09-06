using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing API. Authentication is HTTP Basic with the
/// API key as the username, configured once on the <see cref="HttpClient"/> so the key never
/// travels through this class.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    public const string HttpClientName = "Maxio";

    /// <summary>Maxio caps a page at 200 records.</summary>
    private const int MaxPageSize = 200;

    /// <summary>Guards against an unbounded loop if the API ever stops shrinking pages.</summary>
    private const int MaxPages = 25;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MaxioSite?> ReadSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await GetAsync<MaxioSiteEnvelope>("site.json", allowNotFound: true, cancellationToken);
        return envelope?.Site;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productFamilyHandle))
        {
            throw new ArgumentException("A product family handle is required.", nameof(productFamilyHandle));
        }

        // The family can be addressed by numeric id or by "handle:" prefix. Handles are the
        // stable identifier across sites, so this integration always uses them.
        var familySegment = "handle:" + Uri.EscapeDataString(productFamilyHandle.Trim());
        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = string.Format(
                CultureInfo.InvariantCulture,
                "product_families/{0}/products.json?page={1}&per_page={2}",
                familySegment,
                page,
                MaxPageSize);

            var envelopes = await GetAsync<List<MaxioProductEnvelope>>(path, allowNotFound: true, cancellationToken);
            if (envelopes is null || envelopes.Count == 0)
            {
                break;
            }

            foreach (var envelope in envelopes)
            {
                if (envelope.Product is not null)
                {
                    products.Add(envelope.Product);
                }
            }

            if (envelopes.Count < MaxPageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(reference));
        }

        var path = "customers/lookup.json?reference=" + Uri.EscapeDataString(reference);
        var envelope = await GetAsync<MaxioCustomerEnvelope>(path, allowNotFound: true, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateCustomerRequest { Customer = customer };
        var envelope = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope>("customers.json", request, cancellationToken);

        return envelope?.Customer
            ?? throw new MaxioApiException("Maxio accepted the customer but returned no customer record.", HttpStatusCode.OK);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var path = string.Format(CultureInfo.InvariantCulture, "customers/{0}/subscriptions.json", customerId);
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(path, allowNotFound: true, cancellationToken);

        var subscriptions = new List<MaxioSubscription>();
        if (envelopes is not null)
        {
            foreach (var envelope in envelopes)
            {
                if (envelope.Subscription is not null)
                {
                    subscriptions.Add(envelope.Subscription);
                }
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionAttributes subscription, string? uniquenessToken, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = subscription,
            UniquenessToken = string.IsNullOrWhiteSpace(uniquenessToken) ? null : uniquenessToken
        };

        var envelope = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope>("subscriptions.json", request, cancellationToken);

        return envelope?.Subscription
            ?? throw new MaxioApiException("Maxio accepted the subscription but returned no subscription record.", HttpStatusCode.Created);
    }

    private Task<T?> GetAsync<T>(string path, bool allowNotFound, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        return SendAsync<T>(request, allowNotFound, cancellationToken);
    }

    private Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };

        return SendAsync<TResponse>(request, allowNotFound: false, cancellationToken);
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, bool allowNotFound, CancellationToken cancellationToken)
    {
        // Captured up front so they are still available for logging once the request is gone.
        var method = request.Method;
        var uri = request.RequestUri;

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogError(ex, "Maxio {Method} {Path} did not complete after {ElapsedMs}ms.", method, uri, stopwatch.ElapsedMilliseconds);
            throw new MaxioApiException("The Maxio API could not be reached: " + ex.Message, statusCode: null, innerException: ex);
        }

        using (response)
        {
            _logger.LogInformation(
                "Maxio {Method} {Path} responded {StatusCode} in {ElapsedMs}ms.",
                method,
                uri,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);

            if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
            {
                return default;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errors = ParseErrors(payload);
                var summary = errors.Count > 0
                    ? string.Join(" ", errors)
                    : string.Format(CultureInfo.InvariantCulture, "Maxio returned {0} {1}.", (int)response.StatusCode, response.ReasonPhrase);

                _logger.LogWarning(
                    "Maxio {Method} {Path} failed with {StatusCode}: {Errors}",
                    method,
                    uri,
                    (int)response.StatusCode,
                    summary);

                throw new MaxioApiException(summary, response.StatusCode, errors);
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
                throw new MaxioApiException("The Maxio API returned a response that could not be read.", response.StatusCode, innerException: ex);
            }
        }
    }

    /// <summary>
    /// Pulls the messages out of a Maxio error payload. The API uses an array of strings for
    /// most endpoints and a field-keyed object for a few, and can also answer with plain text
    /// (an authentication failure, for instance), so all three are handled.
    /// </summary>
    internal static IReadOnlyList<string> ParseErrors(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("errors", out var errors))
            {
                return Array.Empty<string>();
            }

            var messages = new List<string>();

            switch (errors.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in errors.EnumerateArray())
                    {
                        var text = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            messages.Add(text!);
                        }
                    }

                    break;

                case JsonValueKind.Object:
                    foreach (var property in errors.EnumerateObject())
                    {
                        var text = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            messages.Add(property.Name + ": " + text);
                        }
                    }

                    break;

                case JsonValueKind.String:
                    var single = errors.GetString();
                    if (!string.IsNullOrWhiteSpace(single))
                    {
                        messages.Add(single!);
                    }

                    break;
            }

            return messages;
        }
        catch (JsonException)
        {
            // Not JSON at all - surface a trimmed version of whatever came back.
            var text = payload!.Trim();
            return new[] { text.Length > 200 ? text.Substring(0, 200) : text };
        }
    }
}
