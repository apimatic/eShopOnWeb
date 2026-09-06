using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Wire;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <inheritdoc cref="IMaxioApiClient"/>
public sealed class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maxio caps <c>per_page</c> at 200 and silently clamps anything larger.</summary>
    private const int MaxPageSize = 200;

    /// <summary>Guards against an unbounded loop if a page never reports itself as the last one.</summary>
    private const int MaxPages = 50;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSiteEnvelope>(HttpMethod.Get, "site.json", null, cancellationToken)
            .ConfigureAwait(false);

        return envelope?.Site
            ?? throw new MaxioApiException(HttpMethod.Get, "site.json", HttpStatusCode.OK,
                new[] { "Response did not contain a site." });
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json" +
                       $"?page={page}&per_page={MaxPageSize}";

            var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken)
                .ConfigureAwait(false);

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

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        try
        {
            var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken)
                .ConfigureAwait(false);
            return envelope?.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Maxio answers 404 for a reference it has never seen. That is a normal outcome for a
            // first-time subscriber, not an error.
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCustomerAttributes customer,
        CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new MaxioCreateCustomerRequest { Customer = customer }, SerializerOptions);

        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken)
            .ConfigureAwait(false);

        return envelope?.Customer
            ?? throw new MaxioApiException(HttpMethod.Post, "customers.json", HttpStatusCode.OK,
                new[] { "Response did not contain a customer." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";

        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, path, null, cancellationToken)
            .ConfigureAwait(false);

        var subscriptions = new List<MaxioSubscription>(envelopes?.Count ?? 0);
        foreach (var envelope in envelopes ?? new List<MaxioSubscriptionEnvelope>())
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscriptionAttributes subscription,
        CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(
            new MaxioCreateSubscriptionRequest { Subscription = subscription }, SerializerOptions);

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post, "subscriptions.json", body, cancellationToken).ConfigureAwait(false);

        return envelope?.Subscription
            ?? throw new MaxioApiException(HttpMethod.Post, "subscriptions.json", HttpStatusCode.OK,
                new[] { "Response did not contain a subscription." });
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string path,
        string? jsonBody,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (jsonBody is not null)
        {
            // A buffered string body (rather than a streaming one) keeps the request replayable,
            // which MaxioResilienceHandler depends on when it retries.
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(method, path, response.StatusCode, ParseErrors(payload));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payload, SerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Could not deserialize the Maxio response for {Method} /{Path}.", method.Method, path);
            throw new MaxioApiException(method, path, response.StatusCode,
                new[] { "Response body was not valid JSON for the expected shape." }, ex);
        }
    }

    /// <summary>
    /// Maxio reports failures either as <c>{"errors":["..."]}</c> or as
    /// <c>{"errors":{"field":"..."}}</c>, and occasionally as <c>{"error":"..."}</c>.
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
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new[] { payload!.Trim() };
            }

            var messages = new List<string>();

            if (root.TryGetProperty("errors", out var errors))
            {
                CollectMessages(errors, messages);
            }

            if (root.TryGetProperty("error", out var error))
            {
                CollectMessages(error, messages);
            }

            return messages.Count > 0 ? messages : new[] { payload!.Trim() };
        }
        catch (JsonException)
        {
            return new[] { payload!.Trim() };
        }
    }

    private static void CollectMessages(JsonElement element, ICollection<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    messages.Add(text!);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectMessages(item, messages);
                }

                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var before = messages.Count;
                    CollectMessages(property.Value, messages);

                    // Prefix field-scoped errors so "cannot be blank" says which field.
                    if (messages is List<string> list)
                    {
                        for (var i = before; i < list.Count; i++)
                        {
                            list[i] = $"{property.Name}: {list[i]}";
                        }
                    }
                }

                break;
        }
    }
}
