using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Thin, transport-level client for the Maxio Advanced Billing REST API. It owns URL shapes,
/// (de)serialization and the translation of Maxio error bodies into
/// <see cref="SubscriptionBillingException"/>. All policy - idempotency, plan validation, mapping
/// to domain models - lives in <see cref="MaxioSubscriptionBillingService"/>.
/// </summary>
internal sealed class MaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
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

    /// <summary>GET /site.json - site currency and billing architecture.</summary>
    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        var envelope = await GetAsync<MaxioSiteEnvelope>("site.json", cancellationToken);
        return envelope?.Site
            ?? throw new SubscriptionBillingException("Maxio returned an empty site payload.");
    }

    /// <summary>GET /product_families/handle:{handle}/products.json - the plan catalog.</summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?per_page=200";

        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new SubscriptionBillingException(
                $"The Maxio product family '{familyHandle}' does not exist on this site. Check the Maxio:ProductFamilyHandle setting.",
                statusCode: 502);
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await ReadAsync<List<MaxioProductEnvelope>>(response, cancellationToken);
        return envelopes?.Select(e => e.Product).OfType<MaxioProduct>().ToList() ?? new List<MaxioProduct>();
    }

    /// <summary>GET /customers/lookup.json?reference= - exact match, or null when unknown.</summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    /// <summary>POST /customers.json - the reference must be unique across the site.</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(request, options: JsonOptions);
        using var response = await SendAsync(HttpMethod.Post, "customers.json", content, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer
            ?? throw new SubscriptionBillingException("Maxio accepted the customer but returned an empty payload.");
    }

    /// <summary>GET /customers/{id}/subscriptions.json.</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", content: null, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<MaxioSubscription>();
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await ReadAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);
        return envelopes?.Select(e => e.Subscription).OfType<MaxioSubscription>().ToList() ?? new List<MaxioSubscription>();
    }

    /// <summary>POST /subscriptions.json.</summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(request, options: JsonOptions);
        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", content, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription
            ?? throw new SubscriptionBillingException("Maxio accepted the subscription but returned an empty payload.");
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };

        try
        {
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            _logger.LogDebug("Maxio {Method} {Path} -> {Status}", method, path, (int)response.StatusCode);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new SubscriptionBillingException(
                "The Maxio billing service could not be reached. Please try again in a moment.",
                statusCode: 503, innerException: ex);
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body)) return default;

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException(
                "Maxio returned a response that could not be understood.", statusCode: 502, innerException: ex);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ParseErrors(body);
        var status = (int)response.StatusCode;

        _logger.LogError("Maxio request failed with {Status}: {Errors}", status,
            errors.Count > 0 ? string.Join("; ", errors) : "<no error detail>");

        if (response.StatusCode == HttpStatusCode.Conflict &&
            errors.Any(e => e.Contains("DuplicateSubmission", StringComparison.OrdinalIgnoreCase)))
        {
            throw new MaxioDuplicateSubmissionException(
                "Maxio rejected this request as a duplicate submission.", errors);
        }

        // 401/403 mean *our* credentials are wrong; that is never the API caller's fault.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new SubscriptionBillingException(
                "The Maxio API rejected the configured credentials. Check the Maxio:ApiKey and Maxio:Subdomain settings.",
                statusCode: 502, errors: errors);
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500)
        {
            throw new SubscriptionBillingException(
                "The Maxio billing service is temporarily unavailable. Please try again in a moment.",
                statusCode: 503, errors: errors);
        }

        var message = errors.Count > 0
            ? string.Join(" ", errors)
            : $"Maxio rejected the request with HTTP {status}.";

        // 422 is the Maxio validation status; surface it to the caller as a 400-class failure.
        throw new SubscriptionBillingException(message,
            statusCode: response.StatusCode == HttpStatusCode.UnprocessableEntity ? 400 : status,
            errors: errors);
    }

    /// <summary>
    /// Maxio reports failures as {"errors": ["..."]} or {"errors": {"field": "..."}} depending on
    /// the endpoint, and occasionally as a bare {"error": "..."}.
    /// </summary>
    internal static IReadOnlyList<string> ParseErrors(string? body)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(body)) return messages;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return messages;

            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                Collect(errors, messages);
            }

            if (document.RootElement.TryGetProperty("error", out var error))
            {
                Collect(error, messages);
            }
        }
        catch (JsonException)
        {
            // Not JSON (an HTML error page from an edge proxy, say) - nothing useful to extract.
        }

        return messages;

        static void Collect(JsonElement element, List<string> into)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) into.Add(value!);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray()) Collect(item, into);
                    break;
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject()) Collect(property.Value, into);
                    break;
            }
        }
    }
}
