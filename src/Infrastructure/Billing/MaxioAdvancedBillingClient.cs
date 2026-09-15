using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// HttpClient wrapper for Maxio Advanced Billing. Auth is HTTP Basic with the API key as
/// username and <c>x</c> as password (spec <c>securitySchemes.BasicAuth</c>). Base URL is
/// <c>https://{site}.chargify.com</c> unless <see cref="MaxioOptions.BaseUrl"/> is set.
/// </summary>
public class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        IOptionsMonitor<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProductDto>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        // GET /product_families/{product_family_id}/products.json
        // product_family_id: "Either the product family's id or its handle prefixed with `handle:`"
        var encodedHandle = Uri.EscapeDataString(productFamilyHandle);
        var products = new List<MaxioProductDto>();
        const int perPage = 200;
        var page = 1;

        while (true)
        {
            var path = $"product_families/handle:{encodedHandle}/products.json?page={page}&per_page={perPage}";
            var pageItems = await SendAsync<List<MaxioProductResponse>>(HttpMethod.Get, path, null, cancellationToken)
                            ?? new List<MaxioProductResponse>();

            foreach (var wrapper in pageItems)
            {
                var dto = ToProductDto(wrapper.Product);
                if (dto != null)
                {
                    products.Add(dto);
                }
            }

            if (pageItems.Count < perPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioProductDto?> ReadProductByHandleAsync(
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        // GET /products/handle/{api_handle}.json
        var path = $"products/handle/{Uri.EscapeDataString(productHandle)}.json";
        try
        {
            var response = await SendAsync<MaxioProductResponse>(HttpMethod.Get, path, null, cancellationToken);
            return ToProductDto(response?.Product);
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomerDto?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        // GET /customers/lookup.json?reference={reference}
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Get, path, null, cancellationToken);
            return ToCustomerDto(response?.Customer);
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomerDto> CreateCustomerAsync(
        MaxioCreateCustomerDto customer,
        CancellationToken cancellationToken = default)
    {
        // POST /customers.json
        var body = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Post, "customers.json", body, cancellationToken);
        var dto = ToCustomerDto(response?.Customer);
        if (dto == null)
        {
            throw new BillingException("Maxio createCustomer returned an empty customer.");
        }

        return dto;
    }

    public async Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        // GET /customers/{customer_id}/subscriptions.json
        var path = $"customers/{customerId}/subscriptions.json";
        var items = await SendAsync<List<MaxioSubscriptionResponse>>(HttpMethod.Get, path, null, cancellationToken)
                    ?? new List<MaxioSubscriptionResponse>();

        var result = new List<MaxioSubscriptionDto>(items.Count);
        foreach (var wrapper in items)
        {
            var dto = ToSubscriptionDto(wrapper.Subscription);
            if (dto != null)
            {
                result.Add(dto);
            }
        }

        return result;
    }

    public async Task<MaxioSubscriptionDto?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        // GET /subscriptions/lookup.json?reference={reference}
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Get, path, null, cancellationToken);
            return ToSubscriptionDto(response?.Subscription);
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(
        MaxioCreateSubscriptionDto subscription,
        CancellationToken cancellationToken = default)
    {
        // POST /subscriptions.json
        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = subscription.ProductHandle,
                CustomerId = subscription.CustomerId,
                CustomerReference = subscription.CustomerReference,
                Reference = subscription.Reference,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod
            }
        };

        var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        var dto = ToSubscriptionDto(response?.Subscription);
        if (dto == null)
        {
            throw new BillingException("Maxio createSubscription returned an empty subscription.");
        }

        return dto;
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string relativePath, object? body, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        options.EnsureConfigured();

        using var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Authorization = CreateBasicAuthHeader(options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        EnsureBaseAddress(options);

        _logger.LogInformation("Maxio {Method} {Path}", method.Method, SanitizePath(relativePath));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingException($"Maxio Advanced Billing request failed: {ex.Message}", ex);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(payload))
                {
                    return default;
                }

                try
                {
                    return JsonSerializer.Deserialize<T>(payload, MaxioJson.SerializerOptions);
                }
                catch (JsonException ex)
                {
                    throw new BillingException("Maxio Advanced Billing returned a response that could not be parsed.", ex);
                }
            }

            var message = FormatError(response.StatusCode, payload);
            var status = MapErrorStatus(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Maxio {Method} {Path} returned 404.", method.Method, SanitizePath(relativePath));
            }
            else
            {
                _logger.LogWarning("Maxio {Method} {Path} failed with {Status}: {Message}",
                    method.Method, SanitizePath(relativePath), (int)response.StatusCode, message);
            }
            throw new BillingException(message, status);
        }
    }

    private void EnsureBaseAddress(MaxioOptions options)
    {
        var resolved = options.ResolveBaseAddress();
        if (_httpClient.BaseAddress == null || _httpClient.BaseAddress != resolved)
        {
            _httpClient.BaseAddress = resolved;
        }
    }

    private static AuthenticationHeaderValue CreateBasicAuthHeader(string apiKey)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    private static HttpStatusCode MapErrorStatus(HttpStatusCode maxioStatus)
    {
        return maxioStatus switch
        {
            HttpStatusCode.NotFound => HttpStatusCode.NotFound,
            HttpStatusCode.Unauthorized => HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.Forbidden => HttpStatusCode.ServiceUnavailable,
            (HttpStatusCode)422 => HttpStatusCode.BadRequest,
            HttpStatusCode.BadRequest => HttpStatusCode.BadRequest,
            _ => HttpStatusCode.BadGateway
        };
    }

    private static string FormatError(HttpStatusCode statusCode, string payload)
    {
        var details = ExtractErrorDetails(payload);
        if (!string.IsNullOrWhiteSpace(details))
        {
            return $"Maxio Advanced Billing returned {(int)statusCode}: {details}";
        }

        return $"Maxio Advanced Billing returned {(int)statusCode}.";
    }

    private static string ExtractErrorDetails(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return Truncate(payload);
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var item in errors.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(item.GetString() ?? string.Empty);
                    }
                }

                return string.Join(" ", parts);
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                var parts = new List<string>();
                foreach (var property in errors.EnumerateObject())
                {
                    parts.Add($"{property.Name}: {property.Value.ToString()}");
                }

                return string.Join(" ", parts);
            }

            if (errors.ValueKind == JsonValueKind.String)
            {
                return errors.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            return Truncate(payload);
        }

        return Truncate(payload);
    }

    private static string Truncate(string value, int max = 400)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max);
    }

    private static string SanitizePath(string path)
    {
        var queryIndex = path.IndexOf('?');
        return queryIndex >= 0 ? path.Substring(0, queryIndex) : path;
    }

    private static MaxioProductDto? ToProductDto(MaxioProduct? product)
    {
        if (product == null || string.IsNullOrWhiteSpace(product.Handle))
        {
            return null;
        }

        return new MaxioProductDto
        {
            Id = product.Id,
            Handle = product.Handle,
            Name = product.Name ?? product.Handle,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? "month",
            ProductFamilyHandle = product.ProductFamily?.Handle,
            IsArchived = product.ArchivedAt != null
        };
    }

    private static MaxioCustomerDto? ToCustomerDto(MaxioCustomer? customer)
    {
        if (customer == null || customer.Id == 0)
        {
            return null;
        }

        return new MaxioCustomerDto
        {
            Id = customer.Id,
            Email = customer.Email,
            Reference = customer.Reference,
            FirstName = customer.FirstName,
            LastName = customer.LastName
        };
    }

    private static MaxioSubscriptionDto? ToSubscriptionDto(MaxioSubscription? subscription)
    {
        if (subscription == null || subscription.Id == 0)
        {
            return null;
        }

        return new MaxioSubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            ProductPriceInCents = subscription.ProductPriceInCents,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CreatedAt = subscription.CreatedAt,
            Reference = subscription.Reference,
            ProductHandle = subscription.Product?.Handle,
            ProductName = subscription.Product?.Name,
            ProductFamilyHandle = subscription.Product?.ProductFamily?.Handle
        };
    }
}
