using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing REST API.
///
/// Base address derivation follows the spec's x-server-configuration block:
///   US production: https://{site}.chargify.com
///   EU production: https://{site}.ebilling.maxio.com
/// <see cref="MaxioOptions.BaseUrl"/> overrides the derived address verbatim when set.
/// Authentication is HTTP Basic where the username is the API key and the password is "x"
/// (per the spec's security scheme).
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private const string BasicPassword = "x";
    private const string FamilyPathParameterPrefix = "handle:";

    private readonly HttpClient _http;
    private readonly Uri _baseUri;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _http = httpClient;
        _baseUri = BuildBaseUri(options.Value);

        var apiKey = options.Value.ApiKey ?? string.Empty;
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:{BasicPassword}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string familyHandle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new MaxioConfigurationException("No Maxio product family handle is configured.");
        }

        return ListProductsForProductFamilyCoreAsync(familyHandle, ct);
    }

    private async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyCoreAsync(string familyHandle, CancellationToken ct)
    {
        var familyPathParam = Uri.EscapeDataString($"{FamilyPathParameterPrefix}{familyHandle}");
        var url = $"/product_families/{familyPathParam}/products.json?include_archived=false";

        using var response = await SendAsync(HttpMethod.Get, url, null, ct);
        var envelopes = await RequireSuccessAsync<List<MaxioProductEnvelope>>(response, ct);
        var products = new List<MaxioProduct>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Product != null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        using var response = await SendAsync(HttpMethod.Get, url, null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await RequireSuccessAsync<MaxioCustomerEnvelope>(response, ct);
        return envelope.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken ct = default)
    {
        var body = new MaxioCreateCustomerRequest { Customer = customer };
        using var response = await SendAsync(HttpMethod.Post, "/customers.json", body, ct);
        var envelope = await RequireSuccessAsync<MaxioCustomerEnvelope>(response, ct);
        return envelope.Customer
            ?? throw new MaxioApiException((int)response.StatusCode, "The Maxio API did not return the created customer.");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken ct = default)
    {
        var body = new MaxioCreateSubscriptionRequest { Subscription = subscription };
        using var response = await SendAsync(HttpMethod.Post, "/subscriptions.json", body, ct);
        var envelope = await RequireSuccessAsync<MaxioSubscriptionEnvelope>(response, ct);
        return envelope.Subscription
            ?? throw new MaxioApiException((int)response.StatusCode, "The Maxio API did not return the created subscription.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken ct = default)
    {
        var url = $"/customers/{customerId}/subscriptions.json";

        using var response = await SendAsync(HttpMethod.Get, url, null, ct);
        var envelopes = await RequireSuccessAsync<List<MaxioSubscriptionEnvelope>>(response, ct);
        var subscriptions = new List<MaxioSubscription>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription != null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string pathAndQuery, object? body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, new Uri(_baseUri, pathAndQuery));
        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: MaxioJson.Options);
        }

        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private async Task<T> RequireSuccessAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildExceptionAsync(response, ct);
        }

        if (response.Content == null)
        {
            throw new MaxioApiException((int)response.StatusCode, "The Maxio API returned an empty response.");
        }

        var payload = await response.Content.ReadFromJsonAsync<T>(MaxioJson.Options, ct);
        if (payload == null)
        {
            throw new MaxioApiException((int)response.StatusCode, "The Maxio API returned an empty response.");
        }

        return payload;
    }

    private static async Task<MaxioApiException> BuildExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var statusCode = (int)response.StatusCode;
        var body = await ReadBodyAsync(response, ct);

        var errors = Array.Empty<string>();
        if (!string.IsNullOrEmpty(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("errors", out var errorsElement))
                {
                    errors = MaxioApiException.ParseErrors(errorsElement).ToArray();
                }
            }
            catch (JsonException)
            {
                // Body is not JSON; fall back to using it verbatim as the message.
            }
        }

        var message = MaxioApiException.BuildMessage(statusCode, body, errors);
        return new MaxioApiException(statusCode, message, errors);
    }

    private static async Task<string?> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return null;
        }
    }

    private static Uri BuildBaseUri(MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: 'Maxio:ApiKey' is missing. Set it (e.g. via user-secrets or the MAXIO_API_KEY environment variable).");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var explicitBase))
            {
                return explicitBase;
            }

            throw new MaxioConfigurationException(
                "Maxio is misconfigured: 'Maxio:BaseUrl' is not a valid absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: neither 'Maxio:BaseUrl' nor 'Maxio:Subdomain' is set.");
        }

        var site = options.Subdomain.Trim();
        var environment = string.IsNullOrWhiteSpace(options.Environment) ? "US" : options.Environment.Trim();
        var host = environment.Equals("US", StringComparison.OrdinalIgnoreCase)
            ? $"{site}.chargify.com"
            : environment.Equals("EU", StringComparison.OrdinalIgnoreCase)
                ? $"{site}.ebilling.maxio.com"
                : null;

        if (host == null)
        {
            throw new MaxioConfigurationException(
                $"Maxio is misconfigured: unsupported 'Maxio:Environment' value '{environment}'. Supported values are US and EU.");
        }

        return new Uri($"https://{host}");
    }
}
