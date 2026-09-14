using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="IMaxioBillingClient"/> implementation over the Maxio Advanced Billing REST API.
/// Endpoints/shapes: POST/GET /customers.json, GET /customers/lookup.json,
/// GET /customers/{id}/subscriptions.json, POST /subscriptions.json,
/// GET /product_families/handle:{handle}/products.json - all confirmed against the Maxio
/// Advanced Billing API reference before implementation.
/// </summary>
public class MaxioApiClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Subscription states that should NOT block re-subscribing to the same plan.
    // Everything else (active, trialing, past_due, unpaid, soft_failure, on_hold, paused, ...)
    // is treated as "still occupying this plan" for de-duplication purposes.
    private static readonly HashSet<string> InactiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var handle = Uri.EscapeDataString(_options.ProductFamilyHandle);
        var envelopes = await GetAsync<List<ProductEnvelope>>($"product_families/handle:{handle}/products.json", cancellationToken);

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => new MaxioPlan(p!.Handle, p.Name, p.PriceInCents, p.Interval, p.IntervalUnit, p.RequireCreditCard))
            .ToList();
    }

    public async Task<MaxioSubscription> SubscribeAsync(string buyerReference, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new MaxioApiException($"Subscription plan '{planHandle}' was not found.", 400);
        }

        var customer = await FindOrCreateCustomerAsync(buyerReference, email, cancellationToken);

        var existingSubscriptions = await ListSubscriptionsForBuyerAsync(buyerReference, cancellationToken);
        var duplicate = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            !InactiveStates.Contains(s.State));
        if (duplicate is not null)
        {
            return duplicate;
        }

        var createRequest = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionWire
            {
                ProductHandle = planHandle,
                CustomerId = customer.Id,
                // These plans are configured with no required payment method (require_credit_card:
                // false); "remittance" collection avoids Maxio auto-attempting an automatic card
                // charge (which would fail with no payment profile on file) for the initial balance.
                PaymentCollectionMethod = "remittance",
            },
        };

        var created = await PostAsync<CreateSubscriptionEnvelope, SubscriptionEnvelope>("subscriptions.json", createRequest, cancellationToken);
        if (created.Subscription is null)
        {
            throw new MaxioApiException("Maxio did not return the created subscription.", 502);
        }

        return MapSubscription(created.Subscription);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForBuyerAsync(string buyerReference, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerAsync(buyerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var envelopes = await GetAsync<List<SubscriptionEnvelope>>($"customers/{customer.Id}/subscriptions.json", cancellationToken);
        return envelopes
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    private async Task<CustomerWire?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer;
    }

    private async Task<CustomerWire> FindOrCreateCustomerAsync(string reference, string email, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(email);
        var createRequest = new CreateCustomerEnvelope
        {
            Customer = new CreateCustomerWire
            {
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
            },
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", createRequest, JsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A customer reference is unique in Maxio. A 422 here most likely means a
            // concurrent request (e.g. a double-click) already created this customer between
            // our lookup and this create - self-heal by re-reading instead of failing.
            var recovered = await FindCustomerAsync(reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioApiException("Maxio did not return the created customer.", 502);
        }

        return envelope.Customer;
    }

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new MaxioApiException("Maxio returned an empty response.", 502);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string relativeUrl, TRequest body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(relativeUrl, body, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
        return result ?? throw new MaxioApiException("Maxio returned an empty response.", 502);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException($"Maxio API call failed with status {(int)response.StatusCode}: {body}", 502);
    }

    private static MaxioSubscription MapSubscription(SubscriptionWire wire)
    {
        return new MaxioSubscription(
            wire.Id,
            wire.State,
            wire.Product?.Handle,
            wire.Product?.Name,
            wire.Product?.PriceInCents,
            wire.CurrentPeriodEndsAt,
            wire.NextAssessmentAt,
            wire.ActivatedAt);
    }

    private static (string FirstName, string LastName) SplitName(string email)
    {
        var localPart = email.Split('@')[0];
        var segments = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

        if (segments.Length >= 2)
        {
            return (Capitalize(segments[0]), Capitalize(segments[^1]));
        }

        var name = segments.Length == 1 ? segments[0] : localPart;
        return (Capitalize(name), "Customer");
    }
}
