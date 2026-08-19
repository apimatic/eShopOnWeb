using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private const int MaxAttempts = 3;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            ConfigureClient(httpClient, options.Value);
        }
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var familyHandle = Uri.EscapeDataString(productFamilyHandle);
        var json = await SendAsync(
            HttpMethod.Get,
            $"product_families/handle:{familyHandle}/products.json?per_page=200",
            body: null,
            cancellationToken);

        var envelopes = MaxioJson.Deserialize<List<MaxioProductEnvelope>>(json);
        var products = new List<MaxioProduct>();
        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await SendAsync(
                HttpMethod.Get,
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
                body: null,
                cancellationToken);

            return MaxioJson.Deserialize<MaxioCustomerEnvelope>(json).Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var json = await SendAsync(
            HttpMethod.Post,
            "customers.json",
            MaxioJson.Serialize(request),
            cancellationToken);

        var customer = MaxioJson.Deserialize<MaxioCustomerEnvelope>(json).Customer;
        if (customer is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio create-customer response was missing a customer.");
        }

        return customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var json = await SendAsync(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            body: null,
            cancellationToken);

        var envelopes = MaxioJson.Deserialize<List<MaxioSubscriptionEnvelope>>(json);
        var subscriptions = new List<MaxioSubscription>();
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var json = await SendAsync(
            HttpMethod.Post,
            "subscriptions.json",
            MaxioJson.Serialize(request),
            cancellationToken);

        var subscription = MaxioJson.Deserialize<MaxioSubscriptionEnvelope>(json).Subscription;
        if (subscription is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio create-subscription response was missing a subscription.");
        }

        return subscription;
    }

    internal static void ConfigureClient(HttpClient httpClient, MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new MaxioConfigurationException(
                "Maxio:ApiKey is not configured. Set MAXIO_API_KEY (or the Maxio:ApiKey user-secret).");
        }

        httpClient.BaseAddress = new Uri(options.ResolveBaseUrl(), UriKind.Absolute);
        httpClient.Timeout = TimeSpan.FromSeconds(100);
        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X")));
    }

    private async Task<string> SendAsync(
        HttpMethod method,
        string relativeUrl,
        string? body,
        CancellationToken cancellationToken)
    {
        if (_httpClient.BaseAddress is null)
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Set MAXIO_API_KEY and MAXIO_SITE_SUBDOMAIN (or Maxio:BaseUrl).");
        }

        HttpResponseMessage? response = null;
        string content = string.Empty;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativeUrl);
            if (body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
                content = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == MaxAttempts)
                {
                    throw new MaxioApiException(HttpStatusCode.GatewayTimeout, "The Maxio Billing API request timed out.");
                }

                await DelayAsync(attempt, cancellationToken);
                continue;
            }
            catch (HttpRequestException ex)
            {
                if (attempt == MaxAttempts)
                {
                    throw new MaxioApiException(HttpStatusCode.BadGateway, $"Unable to reach Maxio Billing API: {ex.Message}");
                }

                await DelayAsync(attempt, cancellationToken);
                continue;
            }

            if (response.StatusCode == (HttpStatusCode)429 && attempt < MaxAttempts)
            {
                _logger.LogWarning("Maxio returned 429 Too Many Requests; retrying (attempt {Attempt}/{Max}).", attempt, MaxAttempts);
                await DelayAsync(attempt, cancellationToken);
                continue;
            }

            break;
        }

        if (response is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "No response from Maxio Billing API.");
        }

        if (response.IsSuccessStatusCode)
        {
            return content;
        }

        throw new MaxioApiException(response.StatusCode, FormatError(response.StatusCode, content));
    }

    private static async Task DelayAsync(int attempt, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        await Task.Delay(delay, cancellationToken);
    }

    private static string FormatError(HttpStatusCode statusCode, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return $"Maxio Billing API returned {(int)statusCode} {statusCode}.";
        }

        try
        {
            var payload = MaxioJson.Deserialize<MaxioErrorPayload>(content);
            if (payload.Errors is { Count: > 0 })
            {
                return string.Join(" ", payload.Errors);
            }
        }
        catch (Exception)
        {
            // Fall through to the raw body.
        }

        return content;
    }
}
