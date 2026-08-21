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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly Uri _baseAddress;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _options.Validate();
        _baseAddress = _options.GetBaseAddress();
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        var response = await SendAsync<List<MaxioProductEnvelope>>(
            HttpMethod.Get,
            $"product_families/{family}/products.json",
            null,
            cancellationToken) ?? throw new MaxioContractException("Maxio returned an empty product list.");

        return response
            .Select(item => item.Product)
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            cancellationToken,
            allowNotFound: true);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomer customer, CancellationToken cancellationToken)
    {
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerBody
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };
        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", request, cancellationToken)
            ?? throw new MaxioContractException("Maxio returned an empty customer response.");
        return envelope.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            cancellationToken,
            allowNotFound: true);
        return envelope?.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken) ?? throw new MaxioContractException("Maxio returned an empty subscription list.");
        return envelopes.Select(envelope => envelope.Subscription).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscription subscription, CancellationToken cancellationToken)
    {
        var request = new CreateSubscriptionRequestBody
        {
            Subscription = new CreateSubscriptionBody
            {
                ProductHandle = subscription.ProductHandle,
                CustomerReference = subscription.CustomerReference,
                Reference = subscription.Reference
            }
        };
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request, cancellationToken)
            ?? throw new MaxioContractException("Maxio returned an empty subscription response.");
        return envelope.Subscription;
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x")));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioTransportException("Maxio Advanced Billing is temporarily unavailable.", exception);
        }

        using (response)
        {
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new MaxioApiException(
                    response.StatusCode,
                    await ReadErrorAsync(response, cancellationToken));
            }

            try
            {
                var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
                return result ?? throw new MaxioContractException("Maxio returned an empty JSON response.");
            }
            catch (JsonException exception)
            {
                throw new MaxioContractException($"Maxio returned an unexpected JSON response: {exception.Message}");
            }
        }
    }

    private Uri BuildUri(string path)
    {
        return new Uri($"{_baseAddress.AbsoluteUri.TrimEnd('/')}/{path.TrimStart('/')}", UriKind.Absolute);
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 2000)
        {
            body = body[..2000];
        }

        return string.IsNullOrWhiteSpace(body)
            ? $"Maxio returned HTTP {(int)response.StatusCode}."
            : $"Maxio returned HTTP {(int)response.StatusCode}: {body}";
    }
}
