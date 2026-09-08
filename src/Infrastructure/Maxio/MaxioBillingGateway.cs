using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="IMaxioBillingGateway"/> implemented over <see cref="HttpClient"/>.
///
/// Every Maxio interaction in this codebase is routed through this class, which is the single
/// place that knows the Maxio wire format: HTTP Basic auth with the API key as user name and
/// <c>X</c> as password ("Authentication" in the Billing API docs) against the
/// <c>https://&lt;subdomain&gt;.chargify.com</c> base address ("Request and Response Data" in the
/// Billing API docs). Resources are addressed as <c>.json</c> endpoints and single/list results
/// are wrapped in named objects.
/// </summary>
public sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private const int MaxPageSize = 200;

    private static readonly JsonSerializerOptions JsonOptions =
        new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioBillingGateway> _logger;
    private readonly string _apiBaseUrl;
    private readonly string _basicAuthHeader;

    public MaxioBillingGateway(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingGateway> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var settings = options.Value;
        bool hasBaseAddress = !string.IsNullOrWhiteSpace(settings.BaseUrl)
            || !string.IsNullOrWhiteSpace(settings.Subdomain);
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || !hasBaseAddress)
        {
            throw new InvalidOperationException(
                "Maxio Advanced Billing is not configured. Set Maxio:ApiKey and Maxio:Subdomain " +
                "(or Maxio:BaseUrl) before using the subscription endpoints.");
        }

        _apiBaseUrl = settings.ApiBaseUrl.TrimEnd('/');
        _basicAuthHeader = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsByFamilyHandleAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        string relativeUrl =
            $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json" +
            $"?per_page={MaxPageSize}";

        using var response = await SendAsync(HttpMethod.Get, relativeUrl, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var items = await DeserializeAsync<List<MaxioProductEnvelope>>(response, cancellationToken);
        return items
            .Where(item => item.Product is not null)
            .Select(item => item.Product)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        string relativeUrl = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        using var response = await SendAsync(HttpMethod.Get, relativeUrl, null, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Maxio customer lookup for reference {Reference} returned 404.", reference);
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioNewCustomer customer,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "customers.json",
            JsonContent(new MaxioCustomerWriteEnvelope { Customer = customer }),
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope.Customer
            ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty customer payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        string relativeUrl = $"customers/{customerId}/subscriptions.json?per_page={MaxPageSize}";

        using var response = await SendAsync(HttpMethod.Get, relativeUrl, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var items = await DeserializeAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);
        return items
            .Where(item => item.Subscription is not null)
            .Select(item => item.Subscription)
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioNewSubscription subscription,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "subscriptions.json",
            JsonContent(new MaxioSubscriptionWriteEnvelope { Subscription = subscription }),
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await DeserializeAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope.Subscription
            ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty subscription payload.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativeUrl,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var requestUri = $"{_apiBaseUrl}/{relativeUrl}";

        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _basicAuthHeader);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = content;

        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static StringContent JsonContent<T>(T payload)
    {
        return new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(
            (int)response.StatusCode,
            $"Maxio API {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} " +
            $"returned HTTP {(int)response.StatusCode}.",
            body);
    }

    private static async Task<T> DeserializeAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an unreadable payload.", json);
    }
}
