using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(HttpClient httpClient, ILogger<MaxioBillingGateway> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200&include_archived=false";
        var envelopes = await SendAsync<List<ProductEnvelope>>(HttpMethod.Get, path, body: null, cancellationToken)
            ?? new List<ProductEnvelope>();

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => MaxioMappings.ToPlan(p!))
            .ToList();
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken)
    {
        var path = $"products/handle/{Uri.EscapeDataString(productHandle)}.json";
        var envelope = await SendAsync<ProductEnvelope>(HttpMethod.Get, path, body: null, cancellationToken, allowNotFound: true);
        if (envelope?.Product is null)
        {
            return null;
        }

        return new MaxioProduct(MaxioMappings.ToPlan(envelope.Product), envelope.Product.ProductFamily?.Handle);
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Get, path, body: null, cancellationToken, allowNotFound: true);
        return envelope?.Customer is null ? null : ToCustomer(envelope.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(NewMaxioCustomer customer, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new CreateCustomerRequestBody
        {
            UniquenessToken = uniquenessToken,
            Customer = new CreateCustomerPayload
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new BillingIntegrationException("Maxio create-customer returned an empty payload.", HttpStatusCode.BadGateway);
        }

        return ToCustomer(envelope.Customer);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await SendAsync<List<SubscriptionEnvelope>>(HttpMethod.Get, path, body: null, cancellationToken)
            ?? new List<SubscriptionEnvelope>();

        return envelopes
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => MaxioMappings.ToSubscription(s!))
            .ToList();
    }

    public async Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Get, path, body: null, cancellationToken, allowNotFound: true);
        return envelope?.Subscription is null ? null : MaxioMappings.ToSubscription(envelope.Subscription);
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(NewMaxioSubscription subscription, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionRequestBody
        {
            UniquenessToken = uniquenessToken,
            Subscription = new CreateSubscriptionPayload
            {
                ProductHandle = subscription.ProductHandle,
                CustomerId = subscription.CustomerId,
                Reference = subscription.Reference
            }
        };

        var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new BillingIntegrationException("Maxio create-subscription returned an empty payload.", HttpStatusCode.BadGateway);
        }

        return MaxioMappings.ToSubscription(envelope.Subscription);
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingIntegrationException("The Maxio Billing API request timed out.", HttpStatusCode.GatewayTimeout, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingIntegrationException("The Maxio Billing API is unreachable.", HttpStatusCode.BadGateway, ex);
        }

        using (response)
        {
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var conflictBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Maxio returned 409 Conflict for {Method} {Path}.", method, path);
                throw new BillingConflictException(string.IsNullOrWhiteSpace(conflictBody)
                    ? "Duplicate Maxio submission."
                    : conflictBody);
            }

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var errors = await ReadErrorsAsync(response, cancellationToken);
                throw new BillingValidationException("Maxio rejected the billing request.", errors);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Maxio request {Method} {Path} failed with {StatusCode}.", method, path, (int)response.StatusCode);
                var mapped = MapFailureStatus(response.StatusCode);
                var summary = string.IsNullOrWhiteSpace(errorBody)
                    ? $"Maxio Billing API returned {(int)response.StatusCode}."
                    : $"Maxio Billing API returned {(int)response.StatusCode}.";
                throw new BillingIntegrationException(summary, mapped);
            }

            if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            {
                return default;
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new BillingIntegrationException("Maxio Billing API returned a payload that could not be parsed.", HttpStatusCode.BadGateway, ex);
            }
        }
    }

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<MaxioErrorBody>(JsonOptions, cancellationToken);
            return FlattenErrors(payload?.Errors);
        }
        catch (JsonException)
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(raw) ? new[] { "Unprocessable billing request." } : new[] { raw };
        }
    }

    private static IReadOnlyList<string> FlattenErrors(object? errors)
    {
        if (errors is null)
        {
            return new[] { "Unprocessable billing request." };
        }

        if (errors is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                return element.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Cast<string>()
                    .ToList();
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                return element.EnumerateObject()
                    .SelectMany(prop =>
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            return prop.Value.EnumerateArray().Select(v => $"{prop.Name}: {v.GetString()}");
                        }

                        return new[] { $"{prop.Name}: {prop.Value}" };
                    })
                    .ToList();
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                var value = element.GetString();
                return string.IsNullOrWhiteSpace(value) ? new[] { "Unprocessable billing request." } : new[] { value };
            }
        }

        return new[] { errors.ToString() ?? "Unprocessable billing request." };
    }

    private static HttpStatusCode MapFailureStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => HttpStatusCode.BadGateway,
        HttpStatusCode.TooManyRequests => HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.NotFound => HttpStatusCode.BadGateway,
        _ when (int)status >= 500 => HttpStatusCode.BadGateway,
        _ => HttpStatusCode.BadGateway
    };

    private static MaxioCustomer ToCustomer(CustomerPayload payload) =>
        new(payload.Id, payload.Reference ?? string.Empty, payload.Email ?? string.Empty,
            payload.FirstName ?? string.Empty, payload.LastName ?? string.Empty);
}
