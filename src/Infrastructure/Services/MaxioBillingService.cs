using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Talks to the Maxio Advanced Billing REST API directly over HTTP (no generated SDK
/// dependency). Endpoints, request/response shapes, and auth scheme were verified against
/// the official ab-dotnet-sdk (https://github.com/maxio-com/ab-dotnet-sdk) before being used
/// here. The injected <see cref="HttpClient"/> is expected to already have its BaseAddress
/// and Basic Auth header configured (see PublicApi's Program.cs AddHttpClient registration).
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    // Subscription states from which re-subscribing to the same plan is allowed. Every other
    // state (active, trialing, past_due, on_hold, etc.) is treated as "already enrolled" so a
    // double-click (or a retried request) never creates a second subscription to the same plan.
    private static readonly HashSet<string> ReSubscribableStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = $"handle:{_options.ProductFamilyHandle}";
        var response = await _httpClient.GetAsync($"/product_families/{Uri.EscapeDataString(familyId)}/products.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var wireProducts = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(cancellationToken: cancellationToken) ?? new();
        return wireProducts
            .Where(p => p.Product is not null)
            .Select(p => new MaxioPlan
            {
                Handle = p.Product!.Handle,
                Name = p.Product.Name,
                Description = p.Product.Description,
                PriceInCents = p.Product.PriceInCents ?? 0,
                Interval = p.Product.Interval ?? 0,
                IntervalUnit = p.Product.IntervalUnit ?? "month"
            })
            .ToList();
    }

    public async Task<MaxioSubscription> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var customerId = await FindOrCreateCustomerAsync(customerReference, customerEmail, cancellationToken);

        var existingSubscriptions = await ListSubscriptionsForCustomerIdAsync(customerId, cancellationToken);
        var existingMatch = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            !ReSubscribableStates.Contains(s.State));
        if (existingMatch is not null)
        {
            return MapSubscription(existingMatch, isNewlyCreated: false);
        }

        var createBody = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionWire { ProductHandle = planHandle, CustomerId = customerId }
        };
        var response = await _httpClient.PostAsJsonAsync("/subscriptions.json", createBody, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var created = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(cancellationToken: cancellationToken);
        if (created?.Subscription is null)
        {
            throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty subscription payload.");
        }

        return MapSubscription(created.Subscription, isNewlyCreated: true);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customerId = await FindCustomerIdByReferenceAsync(customerReference, cancellationToken);
        if (customerId is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = await ListSubscriptionsForCustomerIdAsync(customerId.Value, cancellationToken);
        return subscriptions.Select(s => MapSubscription(s)).ToList();
    }

    private async Task<int> FindOrCreateCustomerAsync(string reference, string email, CancellationToken cancellationToken)
    {
        var existingId = await FindCustomerIdByReferenceAsync(reference, cancellationToken);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        var atIndex = email.IndexOf('@');
        var firstName = atIndex > 0 ? email[..atIndex] : email;

        var body = new CreateCustomerEnvelope
        {
            Customer = new CreateCustomerWire
            {
                FirstName = firstName,
                LastName = "eShopOnWeb",
                Email = email,
                Reference = reference
            }
        };
        var response = await _httpClient.PostAsJsonAsync("/customers.json", body, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Maxio only allows one customer per reference. If a concurrent request (e.g. a
            // double-click) won the race and created the customer between our lookup and this
            // create call, fall back to it instead of surfacing a spurious error.
            var racedId = await FindCustomerIdByReferenceAsync(reference, cancellationToken);
            if (racedId is not null)
            {
                return racedId.Value;
            }
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var createdCustomer = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(cancellationToken: cancellationToken);
        if (createdCustomer?.Customer is null)
        {
            throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty customer payload.");
        }

        return createdCustomer.Customer.Id;
    }

    private async Task<int?> FindCustomerIdByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var wire = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(cancellationToken: cancellationToken);
        return wire?.Customer?.Id;
    }

    private async Task<List<SubscriptionWire>> ListSubscriptionsForCustomerIdAsync(int customerId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var wire = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(cancellationToken: cancellationToken) ?? new();
        return wire.Where(w => w.Subscription is not null).Select(w => w.Subscription!).ToList();
    }

    private static MaxioSubscription MapSubscription(SubscriptionWire wire, bool isNewlyCreated = false) => new()
    {
        SubscriptionId = wire.Id,
        PlanHandle = wire.Product?.Handle ?? string.Empty,
        PlanName = wire.Product?.Name ?? string.Empty,
        PriceInCents = wire.Product?.PriceInCents ?? 0,
        State = wire.State,
        NextBillingAt = wire.NextAssessmentAt,
        IsNewlyCreated = isNewlyCreated
    };

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException((int)response.StatusCode, $"Maxio API request failed with {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }
}
