using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.MaxioModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// HTTP client for Maxio Advanced Billing.
/// Auth: HTTP Basic with API key as username and literal "x" as password.
/// Base URL: Maxio:BaseUrl when set, otherwise https://{Maxio:Subdomain}.chargify.com
/// </summary>
public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProductDto>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        // Product family id-or-handle: "Either the product family's id or its handle prefixed with handle:"
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(SerializerOptions, cancellationToken)
                        ?? new List<ProductEnvelope>();
        var products = new List<ProductDto>();
        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    public async Task<CustomerDto?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(SerializerOptions, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<CustomerDto> CreateCustomerAsync(CreateCustomerBody customer, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var response = await _httpClient.PostAsJsonAsync(
            "customers.json",
            new CreateCustomerEnvelope { Customer = customer },
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(SerializerOptions, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new BillingGatewayException("Maxio returned an empty customer payload.", 502);
        }

        return envelope.Customer;
    }

    public async Task<SubscriptionDto?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(SerializerOptions, cancellationToken);
        return envelope?.Subscription;
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(CreateSubscriptionBody subscription, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var response = await _httpClient.PostAsJsonAsync(
            "subscriptions.json",
            new CreateSubscriptionEnvelope { Subscription = subscription },
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(SerializerOptions, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new BillingGatewayException("Maxio returned an empty subscription payload.", 502);
        }

        return envelope.Subscription;
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(SerializerOptions, cancellationToken)
                        ?? new List<SubscriptionEnvelope>();
        var subscriptions = new List<SubscriptionDto>();
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new BillingConfigurationException(
                "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle (and optionally Maxio:BaseUrl).");
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = MaxioErrorParser.Format(body, (int)response.StatusCode);
        _logger.LogWarning("Maxio API {Method} {Uri} failed with {StatusCode}",
            response.RequestMessage?.Method,
            response.RequestMessage?.RequestUri,
            (int)response.StatusCode);

        var mappedStatus = MapStatus((int)response.StatusCode);
        throw new BillingGatewayException(message, mappedStatus);
    }

    private static int MapStatus(int maxioStatus) =>
        maxioStatus switch
        {
            400 or 401 or 403 or 404 or 409 or 422 => maxioStatus == 401 || maxioStatus == 403 ? 503 : maxioStatus,
            _ => 502
        };
}
