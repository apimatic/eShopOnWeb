using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Talks to the Maxio Advanced Billing REST API directly over HTTP (Basic Auth + JSON),
/// per the contract confirmed against Maxio's own ab-dotnet-sdk/ab-python-sdk sources.
/// The injected <see cref="HttpClient"/> is configured (base address, auth header) by the
/// caller's DI registration, so this class stays agnostic of where the site lives.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly string _productFamilyHandle;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        _httpClient = httpClient;
        _productFamilyHandle = settings.Value.ProductFamilyHandle;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var handle = Uri.EscapeDataString(_productFamilyHandle);
        using var response = await _httpClient.GetAsync($"/product_families/handle:{handle}/products.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(SerializerOptions, cancellationToken)
            ?? new List<ProductEnvelope>();

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => new MaxioPlan
            {
                ProductId = p!.Id,
                Handle = p.Handle,
                Name = p.Name,
                PriceInCents = p.PriceInCents,
                IntervalCount = p.Interval,
                IntervalUnit = p.IntervalUnit,
                // require_credit_card is the actual gate on signup; request_credit_card only
                // controls whether Maxio's own hosted pages show a (non-mandatory) card field.
                PaymentMethodRequired = p.RequireCreditCard
            })
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(SerializerOptions, cancellationToken);
        return ToCustomer(envelope?.Customer);
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerEnvelope
        {
            Customer = new CreateCustomerWire
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("/customers.json", body, SerializerOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio enforces one customer per `reference`. A concurrent request (e.g. a
            // double-click) may have won the race and created it first - fall back to the
            // lookup instead of failing, so subscribing is idempotent.
            var raced = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(SerializerOptions, cancellationToken);
        return ToCustomer(envelope?.Customer) ?? throw new MaxioApiException("Maxio did not return the created customer.");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default)
    {
        var body = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionWire
            {
                ProductHandle = planHandle,
                CustomerId = customerId
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("/subscriptions.json", body, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(SerializerOptions, cancellationToken);
        return ToSubscription(envelope?.Subscription) ?? throw new MaxioApiException("Maxio did not return the created subscription.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(SerializerOptions, cancellationToken)
            ?? new List<SubscriptionEnvelope>();

        return envelopes
            .Select(e => ToSubscription(e.Subscription))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    private static MaxioCustomer? ToCustomer(CustomerWire? wire)
    {
        if (wire is null) return null;
        return new MaxioCustomer
        {
            Id = wire.Id,
            Reference = wire.Reference ?? string.Empty,
            Email = wire.Email ?? string.Empty
        };
    }

    private static MaxioSubscription? ToSubscription(SubscriptionWire? wire)
    {
        if (wire is null) return null;
        return new MaxioSubscription
        {
            Id = wire.Id,
            State = wire.State,
            PlanHandle = wire.Product?.Handle,
            PlanName = wire.Product?.Name,
            PriceInCents = wire.ProductPriceInCents,
            NextBillingAt = wire.NextAssessmentAt
        };
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = ExtractErrorMessage(body) ?? $"Maxio API call failed with status {(int)response.StatusCode}.";
        throw new MaxioApiException(message, (int)response.StatusCode);
    }

    private static string? ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("errors", out var errors)) return body;

            return errors.ValueKind switch
            {
                JsonValueKind.Array => string.Join("; ", errors.EnumerateArray().Select(e => e.ToString())),
                JsonValueKind.Object => string.Join("; ", errors.EnumerateObject().Select(p => $"{p.Name}: {p.Value}")),
                _ => errors.ToString()
            };
        }
        catch (JsonException)
        {
            return body;
        }
    }
}
