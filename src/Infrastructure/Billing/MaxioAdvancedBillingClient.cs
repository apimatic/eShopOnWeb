using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        ConfigureClient(httpClient, options.Value);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200";
        var wrapped = await SendAsync<List<MaxioProductResponse>>(HttpMethod.Get, path, body: null, uniquenessToken: null, cancellationToken);
        return wrapped?
            .Select(item => item.Product)
            .Where(product => product is not null && string.IsNullOrEmpty(product.ArchivedAt))
            .Cast<MaxioProduct>()
            .ToList() ?? new List<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var wrapped = await SendAsync<MaxioCustomerResponse>(HttpMethod.Get, path, body: null, uniquenessToken: null, cancellationToken);
            return wrapped?.Customer;
        }
        catch (BillingProviderException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken = default)
    {
        var wrapped = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Post,
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            uniquenessToken: null,
            cancellationToken);

        return wrapped?.Customer ?? throw new BillingProviderException("Maxio did not return a customer after create.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var wrapped = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            body: null,
            uniquenessToken: null,
            cancellationToken);

        return wrapped?
            .Select(item => item.Subscription)
            .Where(subscription => subscription is not null)
            .Cast<MaxioSubscription>()
            .ToList() ?? new List<MaxioSubscription>();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        string productHandle,
        int customerId,
        string uniquenessToken,
        CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId
            },
            UniquenessToken = uniquenessToken
        };

        var wrapped = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post,
            "subscriptions.json",
            payload,
            uniquenessToken,
            cancellationToken);

        return wrapped?.Subscription ?? throw new BillingProviderException("Maxio did not return a subscription after create.");
    }

    internal static void ConfigureClient(HttpClient httpClient, MaxioOptions options)
    {
        httpClient.BaseAddress = new Uri(ResolveBaseAddress(options), UriKind.Absolute);
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X")));
    }

    internal static string ResolveBaseAddress(MaxioOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            var configured = options.BaseUrl.Trim();
            return configured.EndsWith('/') ? configured : configured + "/";
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            throw new BillingProviderException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        return $"https://{options.Subdomain.Trim()}.chargify.com/";
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        string? uniquenessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("Maxio {Method} {Path}", method.Method, relativePath);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("The billing provider is unavailable.", ex);
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

                return JsonSerializer.Deserialize<T>(payload, MaxioJson.SerializerOptions);
            }

            var message = FormatError(payload, response.StatusCode);
            _logger.LogWarning("Maxio {Method} {Path} failed with {StatusCode}", method.Method, relativePath, (int)response.StatusCode);

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                throw new BillingValidationException(message);
            }

            if (response.StatusCode == HttpStatusCode.Conflict && !string.IsNullOrEmpty(uniquenessToken))
            {
                throw new MaxioDuplicateSubmissionException(uniquenessToken);
            }

            throw new BillingProviderException(message) { StatusCode = (int)response.StatusCode };
        }
    }

    private static string FormatError(string payload, HttpStatusCode statusCode)
    {
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                var errors = JsonSerializer.Deserialize<MaxioErrorResponse>(payload, MaxioJson.SerializerOptions);
                if (errors?.Errors is { Count: > 0 })
                {
                    return string.Join(" ", errors.Errors);
                }
            }
            catch (JsonException)
            {
                // fall through to raw payload
            }
        }

        return string.IsNullOrWhiteSpace(payload)
            ? $"Billing provider returned {(int)statusCode}."
            : payload;
    }
}
