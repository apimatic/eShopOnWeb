using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly TimeSpan ProductFamilyCacheDuration = TimeSpan.FromMinutes(10);

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioClient> _logger;
    private readonly IMemoryCache _cache;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger, IMemoryCache cache)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Maxio is not configured: 'Maxio:ApiKey' is required.");
        }
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio is not configured: 'Maxio:ProductFamilyHandle' is required.");
        }

        _httpClient = httpClient;
        _logger = logger;
        _cache = cache;

        _httpClient.BaseAddress = new Uri(_options.ResolveBaseUrl() + "/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(productFamilyHandle, cancellationToken);
        var wrappers = await GetListAsync<ProductWrapper>($"product_families/{familyId}/products.json", cancellationToken);
        return wrappers.Select(w => w.Product)
            .Where(p => p.ArchivedAt is null)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"customers.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response, cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        MaxioCustomer? customer;
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            var wrappers = JsonSerializer.Deserialize<List<CustomerWrapper>>(content, SerializerOptions);
            customer = wrappers?.FirstOrDefault(w => w.Customer is not null)?.Customer;
        }
        else
        {
            var wrapper = JsonSerializer.Deserialize<CustomerWrapper>(content, SerializerOptions);
            customer = wrapper?.Customer;
        }

        return customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreateRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new CustomerWrapper
        {
            Customer = new MaxioCustomer
            {
                Reference = request.Reference,
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName
            }
        };
        var wrapper = await SendAsync<CustomerWrapper>(HttpMethod.Post, "customers.json", payload, cancellationToken);
        return wrapper.Customer ?? throw new MaxioApiException(500, new List<string> { "Maxio returned an empty customer payload." });
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            _logger.LogInformation("Creating Maxio customer for reference {Reference}", reference);
            return await CreateCustomerAsync(new MaxioCustomerCreateRequest
            {
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            var raced = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation("Maxio customer for reference {Reference} was created concurrently", reference);
                return raced;
            }
            throw;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        var payload = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionPayload
            {
                CustomerId = customerId,
                ProductHandle = productHandle
            }
        };
        var wrapper = await SendAsync<SubscriptionWrapper>(HttpMethod.Post, "subscriptions.json", payload, cancellationToken);
        return wrapper.Subscription ?? throw new MaxioApiException(500, new List<string> { "Maxio returned an empty subscription payload." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var wrappers = await GetListAsync<SubscriptionWrapper>($"customers/{customerId}/subscriptions.json", cancellationToken);
        return wrappers.Select(w => w.Subscription).Where(s => s is not null).Cast<MaxioSubscription>().ToList();
    }

    private async Task<int> ResolveProductFamilyIdAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var cacheKey = $"maxio-product-family-id:{productFamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out int cachedId))
        {
            return cachedId;
        }

        var families = await GetListAsync<ProductFamilyWrapper>("product_families.json", cancellationToken);
        var match = families.FirstOrDefault(f => f.ProductFamily.Handle == productFamilyHandle)?.ProductFamily
            ?? throw new MaxioApiException(404, new List<string> { $"No Maxio product family with handle '{productFamilyHandle}' exists on this site." });

        _cache.Set(cacheKey, match.Id, ProductFamilyCacheDuration);
        return match.Id;
    }

    private async Task<List<T>> GetListAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await DeserializeAsync<List<T>>(response, cancellationToken);
        return result ?? new List<T>();
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken)
            ?? throw new MaxioApiException((int)response.StatusCode, new List<string> { "Maxio returned an unexpected empty payload." });
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = await ExtractErrorsAsync(response, cancellationToken);
        _logger.LogWarning("Maxio API call to {Uri} failed with status {StatusCode}: {Errors}",
            response.RequestMessage?.RequestUri, (int)response.StatusCode, string.Join("; ", errors));

        throw new MaxioApiException((int)response.StatusCode, errors);
    }

    private static async Task<List<string>> ExtractErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(content))
            {
                var envelope = JsonSerializer.Deserialize<MaxioErrorEnvelope>(content, SerializerOptions);
                if (envelope?.Errors is { Count: > 0 })
                {
                    errors.AddRange(envelope.Errors);
                }
                else if (envelope?.FieldErrors is { Count: > 0 })
                {
                    errors.AddRange(envelope.FieldErrors.SelectMany(kv => kv.Value.Select(v => $"{kv.Key}: {v}")));
                }
                else
                {
                    errors.Add(content);
                }
            }
        }
        catch (JsonException)
        {
        }

        if (errors.Count == 0)
        {
            errors.Add($"HTTP {(int)response.StatusCode} ({response.StatusCode.ToString().ToUpper(CultureInfo.InvariantCulture)})");
        }

        return errors;
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }
        return JsonSerializer.Deserialize<T>(content, SerializerOptions);
    }
}
