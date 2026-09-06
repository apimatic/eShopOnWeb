using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <inheritdoc cref="IMaxioApiClient"/>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maxio caps <c>per_page</c> at 200 (components/parameters/per-page.yaml).</summary>
    private const int MaxPerPage = 200;

    /// <summary>
    /// Guard on plan paging. Well beyond any plausible plan catalogue, and it keeps a
    /// misbehaving pagination contract from turning into an unbounded request loop.
    /// </summary>
    private const int MaxProductPages = 25;

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptionsMonitor<MaxioSettings> settings, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<MaxioSiteResponse>("site.json", allowNotFound: false, cancellationToken);
        return response?.Site ?? throw MissingBody(HttpMethod.Get, "site.json", "site");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productFamilyHandle))
        {
            throw new ArgumentException("A product family handle is required.", nameof(productFamilyHandle));
        }

        var basePath = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle.Trim())}/products.json";
        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxProductPages; page++)
        {
            var path = $"{basePath}?page={page}&per_page={MaxPerPage}";
            var pageItems = await GetAsync<List<MaxioProductResponse>>(path, allowNotFound: false, cancellationToken);

            if (pageItems is null || pageItems.Count == 0)
            {
                break;
            }

            foreach (var item in pageItems)
            {
                if (item.Product is not null)
                {
                    products.Add(item.Product);
                }
            }

            if (pageItems.Count < MaxPerPage)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(reference));
        }

        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await GetAsync<MaxioCustomerResponse>(path, allowNotFound: true, cancellationToken);

        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerResponse>("customers.json", request, cancellationToken);
        return response?.Customer ?? throw MissingBody(HttpMethod.Post, "customers.json", "customer");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var response = await GetAsync<List<MaxioSubscriptionResponse>>(path, allowNotFound: true, cancellationToken);

        if (response is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = new List<MaxioSubscription>(response.Count);
        foreach (var item in response)
        {
            if (item.Subscription is not null)
            {
                subscriptions.Add(item.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A subscription reference is required.", nameof(reference));
        }

        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await GetAsync<MaxioSubscriptionResponse>(path, allowNotFound: true, cancellationToken);

        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionResponse>("subscriptions.json", request, cancellationToken);
        return response?.Subscription ?? throw MissingBody(HttpMethod.Post, "subscriptions.json", "subscription");
    }

    private async Task<TResponse?> GetAsync<TResponse>(string path, bool allowNotFound, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync<TResponse>(request, allowNotFound, cancellationToken);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: MaxioSerialization.Options)
        };

        return await SendAsync<TResponse>(request, allowNotFound: false, cancellationToken);
    }

    private async Task<TResponse?> SendAsync<TResponse>(HttpRequestMessage request, bool allowNotFound, CancellationToken cancellationToken)
        where TResponse : class
    {
        var method = request.Method;
        var path = request.RequestUri?.ToString() ?? string.Empty;
        var started = Stopwatch.GetTimestamp();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.CurrentValue.TimeoutSeconds)));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioTransportException(method, path, "the request timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioTransportException(method, path, "the Maxio API could not be reached.", ex);
        }

        using (response)
        {
            _logger.LogInformation(
                "Maxio {Method} {Path} responded {StatusCode} in {ElapsedMs} ms.",
                method, path, (int)response.StatusCode, (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await BuildApiExceptionAsync(method, path, response, cancellationToken);
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                return await JsonSerializer.DeserializeAsync<TResponse>(stream, MaxioSerialization.Options, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new MaxioApiException(method, path, response.StatusCode,
                    new[] { "Maxio returned a body that does not match the contract in maxio-spec/openapi.yaml." }, ex);
            }
        }
    }

    /// <summary>
    /// Reads a Maxio error body into a flat list of messages. The specification uses several error
    /// shapes for the operations this client calls: <c>{"errors": ["..."]}</c>
    /// (Error-List-Response), <c>{"errors": {"customer": "..."}}</c> (Customer-Error-Response), and
    /// a bare JSON string for some 404s. Anything else is reported as raw text, truncated.
    /// </summary>
    private static async Task<MaxioApiException> BuildApiExceptionAsync(
        HttpMethod method, string path, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException)
        {
            return new MaxioApiException(method, path, response.StatusCode, Array.Empty<string>());
        }

        return new MaxioApiException(method, path, response.StatusCode, ParseErrors(body));
    }

    internal static IReadOnlyList<string> ParseErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.String)
            {
                return Wrap(root.GetString());
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors))
            {
                return Flatten(errors);
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var singleError))
            {
                return Flatten(singleError);
            }
        }
        catch (JsonException)
        {
            // Not JSON. Fall through and report the raw text.
        }

        return Wrap(body);
    }

    private static IReadOnlyList<string> Flatten(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return Wrap(element.GetString());

            case JsonValueKind.Array:
                var fromArray = new List<string>();
                foreach (var item in element.EnumerateArray())
                {
                    fromArray.AddRange(Flatten(item));
                }

                return fromArray;

            case JsonValueKind.Object:
                var fromObject = new List<string>();
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var message in Flatten(property.Value))
                    {
                        fromObject.Add($"{property.Name}: {message}");
                    }
                }

                return fromObject;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return Array.Empty<string>();

            default:
                return Wrap(element.ToString());
        }
    }

    private static IReadOnlyList<string> Wrap(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return Array.Empty<string>();
        }

        var trimmed = message.Trim();
        const int maxLength = 500;

        return new[] { trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "…" };
    }

    private static MaxioApiException MissingBody(HttpMethod method, string path, string expectedProperty) =>
        new(method, path, HttpStatusCode.OK,
            new[] { $"Maxio returned a success response without the expected '{expectedProperty}' object." });
}
