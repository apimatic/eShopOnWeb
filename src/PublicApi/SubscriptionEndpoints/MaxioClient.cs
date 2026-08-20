using System;
using System.Collections.Generic;
using System.IO;
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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(string email, string reference, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference,
        CancellationToken cancellationToken);
}

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        var response = await SendAsync(HttpMethod.Get,
            $"product_families/{family}/products.json", null, cancellationToken);
        return (await DeserializeAsync<List<MaxioProductResponse>>(response, cancellationToken))
            .Select(item => item.Product)
            .ToArray();
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken,
            HttpStatusCode.NotFound);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        return (await DeserializeAsync<MaxioCustomerResponse>(response, cancellationToken)).Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string email, string reference,
        CancellationToken cancellationToken)
    {
        var body = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = "eShop",
                LastName = "Customer",
                Email = email,
                Reference = reference
            }
        };
        var response = await SendAsync(HttpMethod.Post, "customers.json", body, cancellationToken);
        return (await DeserializeAsync<MaxioCustomerResponse>(response, cancellationToken)).Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json", null, cancellationToken);
        return (await DeserializeAsync<List<MaxioSubscriptionResponse>>(response, cancellationToken))
            .Select(item => item.Subscription)
            .ToArray();
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken,
            HttpStatusCode.NotFound);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        return (await DeserializeAsync<MaxioSubscriptionResponse>(response, cancellationToken)).Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle,
        string reference, CancellationToken cancellationToken)
    {
        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference
            }
        };
        var response = await SendAsync(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        return (await DeserializeAsync<MaxioSubscriptionResponse>(response, cancellationToken)).Subscription;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body,
        CancellationToken cancellationToken, params HttpStatusCode[] allowedStatuses)
    {
        using var request = new HttpRequestMessage(method, new Uri(_options.GetBaseAddress(), path));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioApiException(HttpStatusCode.GatewayTimeout, "Maxio did not respond before the timeout.");
        }
        catch (HttpRequestException exception)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio could not be reached.", exception);
        }

        if (response.IsSuccessStatusCode || allowedStatuses.Contains(response.StatusCode))
        {
            return response;
        }

        var message = await ReadErrorAsync(response, cancellationToken);
        response.Dispose();
        throw new MaxioApiException(response.StatusCode, message);
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            try
            {
                var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
                return value ?? throw new JsonException("The response body was empty.");
            }
            catch (JsonException exception)
            {
                throw new MaxioApiException(HttpStatusCode.BadGateway,
                    "Maxio returned a response that did not match maxio-spec/openapi.yaml.", exception);
            }
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                return errors.ValueKind switch
                {
                    JsonValueKind.Array => string.Join(" ", errors.EnumerateArray()
                        .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())),
                    JsonValueKind.String => errors.GetString() ?? "Maxio rejected the request.",
                    JsonValueKind.Object => string.Join(" ", errors.EnumerateObject()
                        .Select(item => $"{item.Name}: {item.Value}")),
                    _ => "Maxio rejected the request."
                };
            }
        }
        catch (JsonException)
        {
            // The OpenAPI contract includes a few plain-text error responses.
        }
        catch (IOException)
        {
            // Preserve a safe generic error if an upstream body cannot be read.
        }

        return $"Maxio returned HTTP {(int)response.StatusCode}.";
    }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
