using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing API. All request/response knowledge used
/// here comes from the published Billing API documentation. Authentication is HTTP Basic with
/// the site API key as the username and "x" as the password.
/// </summary>
public class MaxioBillingClient
{
    private const string BasicAuthPassword = "x";

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    private MaxioOptions Options => _options.Value;

    /// <summary>
    /// Looks up a customer by its unique merchant reference. Returns null when the customer
    /// does not exist yet (the Maxio API answers 404 in that case).
    /// </summary>
    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(HttpMethod.Get, $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        using var response = await SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                return (await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken))?.Customer;
            case HttpStatusCode.NotFound:
                return null;
            default:
                throw await BuildApiErrorAsync(response, cancellationToken);
        }
    }

    /// <summary>Creates a customer. Callers must pre-lookup by reference so a single reference maps to one customer.</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerDraft customer, string? uniquenessToken, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(HttpMethod.Post, "/customers.json");
        request.Content = JsonContent.Create(
            new MaxioCustomerCreateRequest { Customer = customer, UniquenessToken = uniquenessToken },
            options: WriteOptions);

        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
        {
            var parsed = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken);
            return parsed?.Customer ?? throw new MaxioApiException("Maxio did not return the created customer.", response.StatusCode);
        }

        throw await BuildApiErrorAsync(response, cancellationToken);
    }

    /// <summary>
    /// Lists the products (plans) belonging to a product family, selected by the family's handle.
    /// </summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsByFamilyHandleAsync(string familyHandle, bool includeArchived, CancellationToken cancellationToken)
    {
        var path = $"/product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json" +
                   $"?include_archived={(includeArchived ? "true" : "false")}&per_page=200";

        using var request = BuildRequest(HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var envelopes = await ReadJsonAsync<List<MaxioProductEnvelope>>(response, cancellationToken);
            return envelopes?
                .Where(e => e.Product is not null)
                .Select(e => e.Product!)
                .ToList() ?? new List<MaxioProduct>();
        }

        throw await BuildApiErrorAsync(response, cancellationToken);
    }

    /// <summary>
    /// Creates a subscription for an existing customer. A uniqueness token makes the creation
    /// idempotent: retrying with the same token within the dedupe window cannot double-subscribe.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionDraft subscription, string? uniquenessToken, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(HttpMethod.Post, "/subscriptions.json");
        request.Content = JsonContent.Create(
            new MaxioSubscriptionCreateRequest { Subscription = subscription, UniquenessToken = uniquenessToken },
            options: WriteOptions);

        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
        {
            var parsed = await ReadJsonAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
            return parsed?.Subscription ?? throw new MaxioApiException("Maxio did not return the created subscription.", response.StatusCode);
        }

        throw await BuildApiErrorAsync(response, cancellationToken);
    }

    /// <summary>Lists every subscription belonging to a customer (all states).</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsByCustomerAsync(long customerId, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(HttpMethod.Get, $"/customers/{customerId}/subscriptions.json");
        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var envelopes = await ReadJsonAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);
            return envelopes?
                .Where(e => e.Subscription is not null)
                .Select(e => e.Subscription!)
                .ToList() ?? new List<MaxioSubscription>();
        }

        throw await BuildApiErrorAsync(response, cancellationToken);
    }

    // ------------------------------------------------------------------------

    private HttpRequestMessage BuildRequest(HttpMethod method, string pathAndQuery)
    {
        if (string.IsNullOrWhiteSpace(Options.ApiKey))
        {
            throw new MaxioConfigurationException(
                "Maxio:ApiKey is not configured. Export the MAXIO_API_KEY environment variable (or set Maxio:ApiKey in user secrets).");
        }

        var request = new HttpRequestMessage(method, ResolveBaseUrl() + pathAndQuery);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(Options.ApiKey + ":" + BasicAuthPassword));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(Options.BaseUrl))
        {
            return Options.BaseUrl!.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Options.Subdomain))
        {
            throw new MaxioConfigurationException(
                "Neither Maxio:BaseUrl nor Maxio:Subdomain is configured. Export the MAXIO_SITE_SUBDOMAIN environment variable (or set Maxio:Subdomain).");
        }

        return $"https://{Options.Subdomain}.chargify.com";
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException(
                $"The Maxio API could not be reached at {request.RequestUri?.GetLeftPart(UriPartial.Path)}: {ex.Message}",
                statusCode: null,
                errors: null,
                innerException: ex);
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(ReadOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(
                "Maxio returned a response body that could not be parsed.",
                (HttpStatusCode?)response.StatusCode,
                null,
                ex);
        }
    }

    private async Task<MaxioApiException> BuildApiErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or ObjectDisposedException)
        {
            // best effort - the status code alone is enough to build a meaningful error
        }

        var errors = MaxioErrors.Parse(body);
        var message = errors is { Count: > 0 }
            ? string.Join(" ", errors)
            : $"Maxio API returned {(int)response.StatusCode} ({(string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase ?? "error" : body)}).";

        _logger.LogWarning(
            "Maxio API returned {StatusCode} for {Method} {Uri}. Errors: {Errors}",
            (int)response.StatusCode,
            response.RequestMessage?.Method,
            response.RequestMessage?.RequestUri,
            string.Join(" | ", errors ?? Array.Empty<string>()));

        return new MaxioApiException(message, response.StatusCode, errors);
    }
}
