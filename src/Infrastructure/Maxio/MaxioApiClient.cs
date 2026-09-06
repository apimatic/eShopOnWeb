using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed <see cref="HttpClient"/> over the Maxio Advanced Billing API, hand-written against
/// <c>maxio-spec/openapi.yaml</c>. Paths, query parameters, request bodies, response envelopes and
/// error models all come from that specification.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Longest slice of a provider error body we will ever echo back or log.</summary>
    private const int MaxErrorBodyLength = 1024;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<MaxioSiteResponse> ReadSiteAsync(CancellationToken cancellationToken = default) =>
        GetRequiredAsync<MaxioSiteResponse>("readSite", "site.json", cancellationToken);

    public async Task<IReadOnlyList<MaxioProductResponse>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        // The spec types product_family_id as a string that is "either the product family's id or its
        // handle prefixed with handle:" - handles are stable across catalog re-seeds, ids are not.
        var path = $"product_families/{Uri.EscapeDataString(productFamilyIdOrHandle)}/products.json" +
                   $"?include_archived={(includeArchived ? "true" : "false")}";

        return await GetRequiredAsync<List<MaxioProductResponse>>("listProductsForProductFamily", path, cancellationToken);
    }

    public Task<MaxioCustomerResponse?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        GetOrNullAsync<MaxioCustomerResponse>(
            "readCustomerByReference",
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);

    public Task<MaxioCustomerResponse> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<MaxioCreateCustomerRequest, MaxioCustomerResponse>("createCustomer", "customers.json", request, cancellationToken);

    public async Task<IReadOnlyList<MaxioSubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";
        return await GetRequiredAsync<List<MaxioSubscriptionResponse>>("listCustomerSubscriptions", path, cancellationToken);
    }

    public Task<MaxioSubscriptionResponse?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default) =>
        GetOrNullAsync<MaxioSubscriptionResponse>(
            "findSubscription",
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);

    public Task<MaxioSubscriptionResponse> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionResponse>("createSubscription", "subscriptions.json", request, cancellationToken);

    private async Task<T> GetRequiredAsync<T>(string operationId, string path, CancellationToken cancellationToken)
    {
        var result = await SendAsync<T>(operationId, () => new HttpRequestMessage(HttpMethod.Get, path), treat404AsNull: false, cancellationToken);
        return result ?? throw EmptyBody(operationId);
    }

    private Task<T?> GetOrNullAsync<T>(string operationId, string path, CancellationToken cancellationToken) =>
        SendAsync<T>(operationId, () => new HttpRequestMessage(HttpMethod.Get, path), treat404AsNull: true, cancellationToken);

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string operationId, string path, TRequest body, CancellationToken cancellationToken)
    {
        // Serialize once, up front, into fully rewindable content so the retry handler can resend the
        // request without re-running serialization.
        var payload = JsonSerializer.SerializeToUtf8Bytes(body, MaxioJson.Options);

        var result = await SendAsync<TResponse>(
            operationId,
            () => new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new ByteArrayContent(payload)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" } }
                }
            },
            treat404AsNull: false,
            cancellationToken);

        return result ?? throw EmptyBody(operationId);
    }

    private async Task<T?> SendAsync<T>(string operationId, Func<HttpRequestMessage> requestFactory, bool treat404AsNull, CancellationToken cancellationToken)
    {
        using var request = requestFactory();
        var stopwatch = Stopwatch.StartNew();

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _logger.LogError(ex, "Maxio operation {OperationId} could not reach the provider after {Elapsed}.", operationId, stopwatch.Elapsed);
            throw new MaxioApiException(operationId, statusCode: null, errors: new[] { ex.Message }, innerException: ex);
        }

        using (response)
        {
            _logger.LogInformation(
                "Maxio {OperationId} {Method} {Path} responded {StatusCode} in {ElapsedMs}ms.",
                operationId,
                request.Method,
                request.RequestUri?.PathAndQuery,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new BillingConfigurationException(
                    $"Maxio rejected the configured credentials (HTTP {(int)response.StatusCode}) on operation '{operationId}'. " +
                    $"Check the {MaxioOptions.SectionName}:{nameof(MaxioOptions.ApiKey)} and {MaxioOptions.SectionName}:{nameof(MaxioOptions.Subdomain)} settings.");
            }

            if (treat404AsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errors = await ReadErrorsAsync(response, cancellationToken);
                throw new MaxioApiException(operationId, (int)response.StatusCode, errors);
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return default;
            }

            try
            {
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonSerializer.DeserializeAsync<T>(stream, MaxioJson.Options, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new MaxioApiException(
                    operationId,
                    (int)response.StatusCode,
                    new[] { "The provider returned a body that does not match the documented schema." },
                    ex);
            }
        }
    }

    private static MaxioApiException EmptyBody(string operationId) =>
        new(operationId, (int)HttpStatusCode.OK, new[] { "The provider returned an empty body." });

    /// <summary>
    /// Reads the provider's error messages. The spec declares two shapes for these: an array of
    /// strings (<c>Error-List-Response.yaml</c>) and an object of field/message pairs
    /// (<c>Customer-Error-Response.yaml</c>). Both are handled; anything else falls back to the raw
    /// body so nothing is silently swallowed.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return Array.Empty<string>();
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errors))
            {
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
                            var text = property.Value.ValueKind == JsonValueKind.String
                                ? property.Value.GetString()
                                : property.Value.ToString();
                            messages.Add($"{property.Name}: {text}");
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

                if (messages.Count > 0)
                {
                    return messages;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON - fall through and surface the raw body instead.
        }

        return new[] { Truncate(body) };
    }

    private static string Truncate(string value) =>
        value.Length <= MaxErrorBodyLength ? value : value.Substring(0, MaxErrorBodyLength) + "...";
}
