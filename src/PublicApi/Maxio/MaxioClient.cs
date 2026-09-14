using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioOptions> _options;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<MaxioSiteEnvelope>(HttpMethod.Get, "site.json", null, cancellationToken);
        return envelope?.Site ?? throw MaxioApiExceptionForMissingContent("site.json");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken)
    {
        string path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?per_page=200";
        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken);
        var products = new List<MaxioProduct>();
        if (envelopes is not null)
        {
            foreach (var envelope in envelopes)
            {
                if (envelope.Product is not null)
                {
                    products.Add(envelope.Product);
                }
            }
        }
        return products;
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            string path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken);
            return envelope?.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken)
    {
        var payload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            }
        };

        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", payload, cancellationToken);
        return envelope?.Customer ?? throw MaxioApiExceptionForMissingContent("customers.json");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        string path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, path, null, cancellationToken);
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

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, CancellationToken cancellationToken)
    {
        var payload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                payment_collection_method = "remittance"
            }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", payload, cancellationToken);
        return envelope?.Subscription ?? throw MaxioApiExceptionForMissingContent("subscriptions.json");
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string relativePath, object? payload, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        if (!options.IsConfigured)
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Set the MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN and MAXIO_DEFAULT_PRODUCT_FAMILY environment variables (bound to the Maxio:ApiKey, Maxio:Subdomain and Maxio:ProductFamilyHandle configuration keys).");
        }

        using var request = new HttpRequestMessage(method, new Uri(options.ResolveBaseUri(), relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (payload is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException((int)response.StatusCode, ReadErrorMessage(body));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(body, SerializerOptions);
    }

    private static string ReadErrorMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "The Maxio Advanced Billing API returned an error.";
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<MaxioErrorEnvelope>(responseBody, SerializerOptions);
            if (envelope?.Errors is { Count: > 0 })
            {
                return string.Join(" ", envelope.Errors);
            }
        }
        catch (JsonException)
        {
        }

        return responseBody.Length <= 500 ? responseBody : responseBody.Substring(0, 500);
    }

    private static MaxioApiException MaxioApiExceptionForMissingContent(string endpoint)
        => new((int)HttpStatusCode.BadGateway, $"The Maxio Advanced Billing API response for '{endpoint}' did not contain the expected content.");
}
