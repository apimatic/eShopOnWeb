using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <inheritdoc cref="IMaxioApiClient"/>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maximum per_page accepted by the specification's paging parameters.</summary>
    private const int MaxPageSize = 200;

    /// <summary>Stops a runaway paging loop if the provider ever ignores the page parameter.</summary>
    private const int MaxPages = 50;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = $"product_families/{Uri.EscapeDataString(productFamilyIdOrHandle)}/products.json" +
                       $"?page={page}&per_page={MaxPageSize}&include_archived=false";

            var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, content: null, cancellationToken)
                            ?? new List<MaxioProductEnvelope>();

            products.AddRange(envelopes.Select(e => e.Product).OfType<MaxioProduct>());

            if (envelopes.Count < MaxPageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, content: null, cancellationToken, treatNotFoundAsNull: true);

        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", request, cancellationToken);

        return envelope?.Customer
               ?? throw new MaxioApiException("Maxio accepted the customer but returned no customer in the response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";

        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, path, content: null, cancellationToken, treatNotFoundAsNull: true);

        return envelopes?.Select(e => e.Subscription).OfType<MaxioSubscription>().ToList()
               ?? (IReadOnlyList<MaxioSubscription>)Array.Empty<MaxioSubscription>();
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get, path, content: null, cancellationToken, treatNotFoundAsNull: true);

        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);

        return envelope?.Subscription
               ?? throw new MaxioApiException("Maxio accepted the subscription but returned no subscription in the response.");
    }

    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        object? content,
        CancellationToken cancellationToken,
        bool treatNotFoundAsNull = false)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(method, path);

        if (content is not null)
        {
            request.Content = JsonContent.Create(content, content.GetType(), options: MaxioSerialization.Options);
        }

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _logger.LogError(ex, "Maxio {Method} {Path} did not complete after {ElapsedMs}ms.",
                method, path, stopwatch.ElapsedMilliseconds);

            throw new MaxioApiException(
                "The Maxio billing service could not be reached.", statusCode: null, providerErrors: null, innerException: ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogInformation("Maxio {Method} {Path} responded {StatusCode} in {ElapsedMs}ms.",
                method, path, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

            if (response.StatusCode == HttpStatusCode.NotFound && treatNotFoundAsNull)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw BuildApiException(method, path, response.StatusCode, body);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<TResponse>(body, MaxioSerialization.Options);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Maxio {Method} {Path} returned a body that could not be read as {Type}.",
                    method, path, typeof(TResponse).Name);

                throw new MaxioApiException(
                    "The Maxio billing service returned an unexpected response.",
                    (int)response.StatusCode, providerErrors: null, innerException: ex);
            }
        }
    }

    private MaxioApiException BuildApiException(HttpMethod method, string path, HttpStatusCode statusCode, string body)
    {
        var errors = ParseErrors(body);
        var status = (int)statusCode;

        _logger.LogWarning("Maxio {Method} {Path} failed with {StatusCode}: {Errors}",
            method, path, status, errors.Count > 0 ? string.Join("; ", errors) : "<no error body>");

        var message = status switch
        {
            401 or 403 => "Maxio rejected the configured API credentials.",
            429 => "Maxio is rate limiting this site; the request was not processed.",
            >= 500 => "Maxio returned a server error.",
            _ => errors.Count > 0
                ? $"Maxio rejected the request: {string.Join("; ", errors)}"
                : $"Maxio rejected the request with status {status}."
        };

        return new MaxioApiException(message, status, errors);
    }

    /// <summary>
    /// Flattens the error shapes the specification defines - an array of strings, a map of
    /// field/message pairs, or a single string - into a flat list of messages.
    /// </summary>
    private static IReadOnlyCollection<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("errors", out var errors))
            {
                return Array.Empty<string>();
            }

            return Flatten(errors, prefix: null).ToList();
        }
        catch (JsonException)
        {
            // Not every failure returns JSON (proxies, gateways); keep whatever text there was.
            return new[] { Truncate(body, 500) };
        }
    }

    private static IEnumerable<string> Flatten(JsonElement element, string? prefix)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return prefix is null ? text! : $"{prefix}: {text}";
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var message in Flatten(item, prefix))
                    {
                        yield return message;
                    }
                }
                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var message in Flatten(property.Value, property.Name))
                    {
                        yield return message;
                    }
                }
                break;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    /// <summary>
    /// Builds the Authorization header value for the specification's BasicAuth scheme:
    /// the API key as the user name, with the fixed password "x".
    /// </summary>
    public static string BuildBasicAuthParameter(string apiKey) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:x"));
}
