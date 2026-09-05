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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP implementation of <see cref="IMaxioClient"/> against the Maxio Advanced Billing (Chargify)
/// REST API. Authentication and base address are configured on the injected <see cref="HttpClient"/>
/// by the DI registration in <see cref="Dependencies"/>.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<MaxioCustomerDto> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var payload = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerAttributes
            {
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", payload, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio only rejects a create with 422 here because the reference is already taken -
            // i.e. a concurrent request (a double-click) won the race. Read back its result.
            var racedCustomer = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (racedCustomer != null)
            {
                return racedCustomer;
            }
        }

        await EnsureSuccessAsync(response, "create customer", cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        return MapCustomer(envelope!.Customer);
    }

    public async Task<IReadOnlyList<MaxioPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = Uri.EscapeDataString(_options.ProductFamilyHandle);
        using var response = await _httpClient.GetAsync($"product_families/handle:{familyHandle}/products.json", cancellationToken);
        await EnsureSuccessAsync(response, "list plans", cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioProductEnvelope>>(JsonOptions, cancellationToken);
        return (envelopes ?? new List<MaxioProductEnvelope>())
            .Select(e => MapPlan(e.Product))
            .ToList();
    }

    // Plans in this integration are configured with no required payment method, but a site's
    // default payment_collection_method ("automatic") still demands a card to actually collect
    // the recurring charge. Falling back to a collection method that defers billing to an
    // invoice lets signup succeed with no payment profile, on either site architecture
    // (Relationship Invoicing uses "remittance"; legacy statement-based sites use "invoice").
    private static readonly string?[] PaymentCollectionMethodAttempts = { null, "remittance", "invoice" };

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default)
    {
        HttpStatusCode? lastStatusCode = null;
        string lastBody = string.Empty;

        foreach (var paymentCollectionMethod in PaymentCollectionMethodAttempts)
        {
            var payload = new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscriptionAttributes
                {
                    CustomerId = customerId,
                    ProductHandle = planHandle,
                    PaymentCollectionMethod = paymentCollectionMethod
                }
            };

            using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, JsonOptions, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(JsonOptions, cancellationToken);
                return MapSubscription(envelope!.Subscription);
            }

            lastStatusCode = response.StatusCode;
            lastBody = await response.Content.ReadAsStringAsync(cancellationToken);

            var isMissingPaymentMethod = response.StatusCode == HttpStatusCode.UnprocessableEntity &&
                lastBody.Contains("payment method", StringComparison.OrdinalIgnoreCase);
            if (!isMissingPaymentMethod)
            {
                break;
            }
        }

        throw new MaxioApiException(
            $"Maxio request to create subscription failed with status {(int)lastStatusCode!.Value} ({lastStatusCode}): {lastBody}",
            lastStatusCode);
    }

    public async Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionEnvelope>>(JsonOptions, cancellationToken);
        return (envelopes ?? new List<MaxioSubscriptionEnvelope>())
            .Select(e => MapSubscription(e.Subscription))
            .ToList();
    }

    private async Task<MaxioCustomerDto?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up customer by reference", cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope == null ? null : MapCustomer(envelope.Customer);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(
            $"Maxio request to {action} failed with status {(int)response.StatusCode} ({response.StatusCode}): {body}",
            response.StatusCode);
    }

    private static MaxioCustomerDto MapCustomer(MaxioCustomerWire wire) => new()
    {
        Id = wire.Id,
        Reference = wire.Reference,
        Email = wire.Email
    };

    private static MaxioPlanDto MapPlan(MaxioProductWire wire) => new()
    {
        Id = wire.Id,
        Handle = wire.Handle ?? string.Empty,
        Name = wire.Name,
        Description = wire.Description,
        PriceInCents = wire.PriceInCents,
        Interval = wire.Interval,
        IntervalUnit = wire.IntervalUnit
    };

    private static MaxioSubscriptionDto MapSubscription(MaxioSubscriptionWire wire) => new()
    {
        Id = wire.Id,
        CustomerId = wire.Customer.Id,
        State = wire.State,
        ProductHandle = wire.Product?.Handle,
        ProductName = wire.Product?.Name,
        ProductPriceInCents = wire.Product?.PriceInCents,
        CurrentPeriodEndsAt = wire.CurrentPeriodEndsAt,
        NextAssessmentAt = wire.NextAssessmentAt
    };
}
