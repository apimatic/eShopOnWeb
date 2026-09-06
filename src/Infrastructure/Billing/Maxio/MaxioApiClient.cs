using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing REST API.
/// </summary>
/// <remarks>
/// Authentication is HTTP Basic with the API key as the user name and the fixed literal <c>x</c> as
/// the password, which is the scheme Advanced Billing documents. The header is attached per request
/// rather than baked into the client so a rotated key takes effect without a process restart, and
/// the key itself is never logged.
/// </remarks>
internal sealed class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Documented password placeholder for Maxio API-key Basic authentication.</summary>
    private const string ApiKeyPasswordPlaceholder = "x";

    /// <summary>Maxio caps <c>per_page</c> at 200 and silently lowers anything above it.</summary>
    private const int MaxPageSize = 200;

    /// <summary>Stops a paging loop from running away if the provider ever ignores <c>page</c>.</summary>
    private const int MaxPages = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettingsProvider _settingsProvider;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, MaxioSettingsProvider settingsProvider, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settingsProvider = settingsProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json" +
                       $"?page={page}&per_page={MaxPageSize}";

            var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, content: null, cancellationToken)
                            ?? new List<MaxioProductEnvelope>();

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

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        FindAsync<MaxioCustomerEnvelope, MaxioCustomer>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            envelope => envelope.Customer,
            cancellationToken);

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes attributes, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post,
            "customers.json",
            new MaxioCreateCustomerRequest(attributes),
            cancellationToken).ConfigureAwait(false);

        return envelope?.Customer
               ?? throw new BillingProviderException("Maxio accepted the customer but returned no customer in the response.");
    }

    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        FindAsync<MaxioSubscriptionEnvelope, MaxioSubscription>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            envelope => envelope.Subscription,
            cancellationToken);

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionAttributes attributes, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest(attributes),
            cancellationToken).ConfigureAwait(false);

        return envelope?.Subscription
               ?? throw new BillingProviderException("Maxio accepted the subscription but returned no subscription in the response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            content: null,
            cancellationToken).ConfigureAwait(false);

        var subscriptions = new List<MaxioSubscription>();
        foreach (var envelope in envelopes ?? new List<MaxioSubscriptionEnvelope>())
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    /// <summary>
    /// Runs a lookup that answers 404 when nothing matches, translating that into <c>null</c>.
    /// </summary>
    private async Task<TResult?> FindAsync<TEnvelope, TResult>(
        string path,
        Func<TEnvelope, TResult?> select,
        CancellationToken cancellationToken)
        where TEnvelope : class
        where TResult : class
    {
        try
        {
            var envelope = await SendAsync<TEnvelope>(HttpMethod.Get, path, content: null, cancellationToken).ConfigureAwait(false);
            return envelope is null ? null : select(envelope);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<TResponse?> SendAsync<TResponse>(HttpMethod method, string path, object? content, CancellationToken cancellationToken)
    {
        var settings = _settingsProvider.GetValidated();
        var pathForDiagnostics = PathWithoutQuery(path);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = BuildAuthorizationHeader(settings.ApiKey!);

        if (content is not null)
        {
            // Pre-serialised into a byte-backed content so the transient-fault handler can resend it.
            request.Content = new StringContent(
                JsonSerializer.Serialize(content, content.GetType(), JsonOptions),
                Encoding.UTF8,
                "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new BillingProviderException(
                $"Could not reach Maxio for {method} {pathForDiagnostics}: {ex.Message}", ex);
        }

        using (response)
        {
            var requestId = ReadRequestId(response);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new MaxioApiException(
                    new HttpMethodAndPath(method.Method, pathForDiagnostics),
                    response.StatusCode,
                    ParseErrors(body),
                    body,
                    requestId);
            }

            _logger.LogDebug("Maxio {Method} {Path} -> {StatusCode} (request id {RequestId}).",
                method, pathForDiagnostics, (int)response.StatusCode, requestId ?? "n/a");

            if (string.IsNullOrWhiteSpace(body))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new BillingProviderException(
                    $"Could not read the Maxio response to {method} {pathForDiagnostics}.",
                    ex,
                    response.StatusCode,
                    requestId);
            }
        }
    }

    private static AuthenticationHeaderValue BuildAuthorizationHeader(string apiKey)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{ApiKeyPasswordPlaceholder}"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }

    private static string? ReadRequestId(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Request-Id", out var values)
            ? string.Join(",", values)
            : null;

    /// <summary>
    /// Pulls the messages out of the Maxio error envelope. The provider mostly answers
    /// <c>{"errors":["..."]}</c>, but also uses <c>{"error":"..."}</c> and, on some routes, a
    /// per-field map; anything that does not parse falls back to an empty list so the raw body is
    /// reported instead.
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<string>();
            }

            var messages = new List<string>();

            if (document.RootElement.TryGetProperty("error", out var single) && single.ValueKind == JsonValueKind.String)
            {
                messages.Add(single.GetString()!);
            }

            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                CollectStrings(errors, messages);
            }

            return messages;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static void CollectStrings(JsonElement element, List<string> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                into.Add(element.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectStrings(item, into);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var before = into.Count;
                    CollectStrings(property.Value, into);
                    for (var i = before; i < into.Count; i++)
                    {
                        into[i] = $"{property.Name}: {into[i]}";
                    }
                }

                break;
        }
    }

    private static string PathWithoutQuery(string path)
    {
        var separator = path.IndexOf('?');
        return separator < 0 ? path : path[..separator];
    }
}
