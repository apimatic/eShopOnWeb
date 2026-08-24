using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Low-level typed client for the Maxio Advanced Billing REST API.
/// Registered as a typed HttpClient; base address and Basic authentication
/// (API key as username, "x" as password) are configured at registration.
/// </summary>
public class MaxioClient
{
    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MaxioProductFamilyDto> GetProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        var wrapper = await GetAsync<MaxioProductFamilyWrapper>(
            $"product_families/lookup.json?handle={Uri.EscapeDataString(handle)}", cancellationToken);
        return wrapper.ProductFamily;
    }

    public async Task<IReadOnlyList<MaxioProductDto>> ListProductsAsync(long productFamilyId, CancellationToken cancellationToken = default)
    {
        var wrappers = await GetAsync<List<MaxioProductWrapper>>(
            $"product_families/{productFamilyId}/products.json", cancellationToken);
        return wrappers.Select(w => w.Product).ToList();
    }

    public async Task<MaxioCustomerDto?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadAsync<MaxioCustomerWrapper>(response, cancellationToken) is { } wrapper
            ? wrapper.Customer
            : null;
    }

    public async Task<MaxioCustomerDto> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var body = new CreateMaxioCustomerRequest
        {
            Customer = new CreateMaxioCustomer
            {
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            }
        };

        var wrapper = await PostAsync<MaxioCustomerWrapper>("customers.json", body, cancellationToken);
        return wrapper.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscriptionDto>> ListSubscriptionsByCustomerAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var wrappers = await GetAsync<List<MaxioSubscriptionWrapper>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);
        return wrappers.Select(w => w.Subscription).ToList();
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        var body = new CreateMaxioSubscriptionRequest
        {
            Subscription = new CreateMaxioSubscription
            {
                CustomerId = customerId,
                ProductHandle = productHandle
            }
        };

        var wrapper = await PostAsync<MaxioSubscriptionWrapper>("subscriptions.json", body, cancellationToken);
        return wrapper.Subscription;
    }

    private async Task<T> GetAsync<T>(string relativeUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativeUri, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<T> PostAsync<T>(string relativeUri, object body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(relativeUri, body, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, await ReadErrorsAsync(response, cancellationToken));
        }

        var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        return result ?? throw new MaxioApiException(response.StatusCode, new[] { "Maxio returned an empty response body." });
    }

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<MaxioErrorResponse>(body);
                if (parsed?.Errors is { Count: > 0 } errors)
                {
                    return errors;
                }
            }
            catch (JsonException)
            {
                // fall through to raw body
            }

            return new[] { body.Length <= 500 ? body : body.Substring(0, 500) };
        }

        return new[] { response.ReasonPhrase ?? "Unknown Maxio API error." };
    }
}
