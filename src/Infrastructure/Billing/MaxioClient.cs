using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Billing.Contracts;
using Microsoft.eShopWeb.ApplicationCore.Billing.Models;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Json;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MaxioClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";
        var json = await ReadAsync(HttpMethod.Get, path, "listProductsForProductFamily", cancellationToken);

        var products = JsonSerializer.Deserialize<List<ProductEnvelope>>(json, ReadOptions)
            ?? new List<ProductEnvelope>();

        return products
            .Where(p => p.Product is not null)
            .Select(p => MapPlan(p.Product!))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await _http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var json = await EnsureSuccessAsync(response, "readCustomerByReference", cancellationToken);
        var envelope = JsonSerializer.Deserialize<CustomerEnvelope>(json, ReadOptions);

        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerCommand command, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            customer = new
            {
                first_name = command.FirstName,
                last_name = command.LastName,
                email = command.Email,
                reference = command.Reference
            }
        };

        var json = await SendJsonAsync(HttpMethod.Post, "customers.json", "createCustomer", payload, cancellationToken);
        var envelope = JsonSerializer.Deserialize<CustomerEnvelope>(json, ReadOptions);

        if (envelope?.Customer is null)
        {
            throw new MaxioApiException("createCustomer", HttpStatusCode.BadGateway, json);
        }

        return MapCustomer(envelope.Customer);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var json = await ReadAsync(HttpMethod.Get, path, "listCustomerSubscriptions", cancellationToken);

        var subs = JsonSerializer.Deserialize<List<SubscriptionEnvelope>>(json, ReadOptions)
            ?? new List<SubscriptionEnvelope>();

        return subs
            .Where(s => s.Subscription is not null)
            .Select(s => MapSubscription(s.Subscription!))
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionCommand command, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            subscription = new
            {
                product_handle = command.ProductHandle,
                customer_id = command.CustomerId,
                payment_collection_method = command.PaymentCollectionMethod
            }
        };

        var json = await SendJsonAsync(HttpMethod.Post, "subscriptions.json", "createSubscription", payload, cancellationToken);
        var envelope = JsonSerializer.Deserialize<SubscriptionEnvelope>(json, ReadOptions);

        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException("createSubscription", HttpStatusCode.BadGateway, json);
        }

        return MapSubscription(envelope.Subscription);
    }

    private async Task<string> ReadAsync(HttpMethod method, string path, string operation, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        var response = await _http.SendAsync(request, cancellationToken);
        return await EnsureSuccessAsync(response, operation, cancellationToken);
    }

    private async Task<string> SendJsonAsync(HttpMethod method, string path, string operation, object payload, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(payload, WriteOptions);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };
        var response = await _http.SendAsync(request, cancellationToken);
        return await EnsureSuccessAsync(response, operation, cancellationToken);
    }

    private static async Task<string> EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        var errors = TryExtractErrors(body);
        throw new MaxioApiException(operation, response.StatusCode, body, errors);
    }

    private static IReadOnlyList<string>? TryExtractErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("errors", out var errors))
            {
                return null;
            }

            switch (errors.ValueKind)
            {
                case JsonValueKind.Array:
                    return errors.EnumerateArray()
                        .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.GetRawText())
                        .ToList();
                case JsonValueKind.Object:
                    return errors.EnumerateObject()
                        .Select(p => $"{p.Name}: {(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText())}")
                        .ToList();
                case JsonValueKind.String:
                    return new List<string> { errors.GetString() ?? string.Empty };
                default:
                    return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static MaxioCustomer MapCustomer(CustomerDto dto)
        => new(dto.Id, dto.FirstName, dto.LastName, dto.Email, dto.Reference);

    private static MaxioPlan MapPlan(ProductDto dto)
        => new(dto.Id, dto.Handle, dto.Name, dto.Description, dto.PriceInCents, dto.Interval, dto.IntervalUnit, dto.Taxable, dto.RequireCreditCard, dto.ArchivedAt);

    private static MaxioSubscription MapSubscription(SubscriptionDto dto)
        => new(
            dto.Id,
            dto.State,
            dto.Customer?.Id ?? 0,
            dto.Product?.Handle,
            dto.Product?.Name ?? string.Empty,
            dto.Product?.PriceInCents ?? 0,
            dto.Product?.Interval,
            dto.Product?.IntervalUnit,
            dto.CurrentPeriodEndsAt,
            dto.NextAssessmentAt,
            dto.ActivatedAt,
            dto.CreatedAt,
            dto.BalanceInCents,
            dto.PaymentCollectionMethod);
}
