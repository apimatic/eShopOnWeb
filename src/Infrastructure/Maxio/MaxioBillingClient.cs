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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Dto;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing API. Every endpoint, request and
/// response shape follows the Maxio OpenAPI specification (maxio-spec/openapi.yaml):
/// Basic auth (API key as username, "x" as password), ".json" suffixed paths and
/// the spec's wrapper objects (e.g. { "subscription": { ... } }).
/// </summary>
public class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioBillingClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // The spec allows the family path parameter to be an id or "handle:{handle}".
        var products = await SendAsync<List<ProductResponseDto>>(
            HttpMethod.Get,
            $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json",
            body: null,
            cancellationToken: cancellationToken);

        return products
            .Where(p => p.Product is not null)
            .Select(p => Map(p.Product!))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var dto = await ReadAsync<CustomerResponseDto>(response, cancellationToken);
        if (dto?.Customer is null)
        {
            throw new MaxioApiException((int)response.StatusCode, "Maxio returned an unexpected customer lookup response.");
        }

        return Map(dto.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default)
    {
        var request = new CreateCustomerRequestDto
        {
            Customer = new CreateCustomerDto
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var dto = await SendAsync<CustomerResponseDto>(HttpMethod.Post, "customers.json", request, cancellationToken);
        if (dto?.Customer is null)
        {
            throw new MaxioApiException((int)HttpStatusCode.InternalServerError, "Maxio returned an unexpected create-customer response.");
        }

        return Map(dto.Customer);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsByCustomerAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await SendAsync<List<SubscriptionResponseDto>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            body: null,
            cancellationToken: cancellationToken);

        return subscriptions
            .Where(s => s.Subscription is not null)
            .Select(s => Map(s.Subscription!))
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionRequestDto
        {
            Subscription = new CreateSubscriptionDto
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference
            }
        };

        var dto = await SendAsync<SubscriptionResponseDto>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        if (dto?.Subscription is null)
        {
            throw new MaxioApiException((int)HttpStatusCode.InternalServerError, "Maxio returned an unexpected create-subscription response.");
        }

        return Map(dto.Subscription);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken)
            ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty response body.");
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException((int)response.StatusCode, await ReadErrorsAsync(response, cancellationToken));
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    // The spec models errors either as a list of messages (Error-List-Response)
    // or as an object keyed by field (Customer-Error-Response); normalize both.
    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return new List<string> { content };
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                return errors.EnumerateArray().Select(e => e.ToString()).ToList();
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                return errors.EnumerateObject().Select(p => $"{p.Name}: {p.Value}").ToList();
            }

            return new List<string> { errors.ToString() };
        }
        catch (JsonException)
        {
            return new List<string> { $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}" };
        }
    }

    private static MaxioProduct Map(ProductDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        Handle = dto.Handle,
        Description = dto.Description,
        PriceInCents = dto.PriceInCents,
        Interval = dto.Interval,
        IntervalUnit = dto.IntervalUnit,
        ArchivedAt = dto.ArchivedAt
    };

    private static MaxioCustomer Map(CustomerDto dto) => new()
    {
        Id = dto.Id,
        FirstName = dto.FirstName,
        LastName = dto.LastName,
        Email = dto.Email,
        Reference = dto.Reference
    };

    private static MaxioSubscription Map(SubscriptionDto dto) => new()
    {
        Id = dto.Id,
        State = dto.State,
        Reference = dto.Reference,
        CurrentPeriodEndsAt = dto.CurrentPeriodEndsAt,
        CreatedAt = dto.CreatedAt,
        CustomerId = dto.Customer?.Id ?? 0,
        ProductId = dto.Product?.Id ?? 0,
        ProductHandle = dto.Product?.Handle ?? string.Empty,
        ProductName = dto.Product?.Name ?? string.Empty,
        ProductPriceInCents = dto.Product?.PriceInCents ?? 0,
        ProductInterval = dto.Product?.Interval ?? 0,
        ProductIntervalUnit = dto.Product?.IntervalUnit ?? string.Empty
    };
}
