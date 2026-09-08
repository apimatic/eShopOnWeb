using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioApiClient : IMaxioClient
{
    private const string RemittanceCollectionMethod = "remittance";
    private const string DoNotReceiveInvoiceEmails = "false";

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;
    private readonly MaxioOptions _options;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<MaxioCustomerEnvelope?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        string path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using HttpResponseMessage response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadSuccessAsync<MaxioCustomerEnvelope>(response, cancellationToken);
    }

    public async Task<MaxioCustomerEnvelope> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateCustomerEnvelope { Customer = customer };
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("customers.json", payload, cancellationToken);
        return await ReadSuccessAsync<MaxioCustomerEnvelope>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<MaxioProductFamilyEnvelope>> ListProductFamiliesAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync("product_families.json", cancellationToken);
        return await ReadSuccessAsync<List<MaxioProductFamilyEnvelope>>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<MaxioProductEnvelope>> ListProductsForFamilyAsync(int productFamilyId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync($"product_families/{productFamilyId}/products.json", cancellationToken);
        return await ReadSuccessAsync<List<MaxioProductEnvelope>>(response, cancellationToken);
    }

    public async Task<MaxioSubscriptionEnvelope?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        string path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using HttpResponseMessage response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadSuccessAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
    }

    public async Task<MaxioSubscriptionEnvelope> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscription.PaymentCollectionMethod))
        {
            subscription.PaymentCollectionMethod = RemittanceCollectionMethod;
        }

        if (string.IsNullOrWhiteSpace(subscription.ReceivesInvoiceEmails))
        {
            subscription.ReceivesInvoiceEmails = DoNotReceiveInvoiceEmails;
        }

        var payload = new MaxioCreateSubscriptionEnvelope { Subscription = subscription };
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, cancellationToken);
        return await ReadSuccessAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<MaxioSubscriptionEnvelope>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        return await ReadSuccessAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);
    }

    private async Task<T> ReadSuccessAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            try
            {
                T? result = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
                if (result is null)
                {
                    throw new MaxioApiException(response.StatusCode, new[] { "Maxio API returned an empty response body." });
                }

                return result;
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new MaxioApiException(response.StatusCode, new[] { $"Maxio API returned an unparseable response: {ex.Message}" });
            }
        }

        var errors = MaxioApiErrorParser.ParseErrors(content);
        _logger.LogWarning("Maxio API request to {Uri} failed with status {StatusCode}: {Errors}",
            response.RequestMessage?.RequestUri, (int)response.StatusCode, string.Join("; ", errors));
        throw new MaxioApiException(response.StatusCode, errors);
    }
}
