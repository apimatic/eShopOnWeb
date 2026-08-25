using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by the Maxio Advanced Billing JSON API.
/// Maxio is the system of record: the eShopOnWeb username is stored as the Maxio customer
/// reference, which makes customer creation and subscription enrollment idempotent.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    // States in which a subscription is live and billing; used to detect an existing enrollment.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "past_due", "on_hold"
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListSubscriptionPlansAsync(CancellationToken cancellationToken = default)
    {
        // The product family is addressed by its stable handle ("handle:<family-handle>") because
        // numeric ids are reassigned whenever the sandbox catalog is re-seeded.
        var url = $"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json?per_page=200";
        var products = await GetAsync<List<MaxioProductResponse>>(url, cancellationToken) ?? new List<MaxioProductResponse>();

        return products
            .Select(p => p.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => new SubscriptionPlan
            {
                ProductId = p!.Id,
                Handle = p.Handle ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Description = p.Description ?? string.Empty,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty
            })
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    public async Task<ShopperSubscription> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(command);
        Guard.Against.NullOrEmpty(command.CustomerReference);
        Guard.Against.NullOrEmpty(command.ProductHandle);

        var customer = await EnsureCustomerAsync(command, cancellationToken);

        // Idempotency: if the shopper already holds a live subscription to this plan, return it
        // instead of enrolling them a second time (e.g. double-click / retry).
        var subscriptions = await ListSubscriptionsByCustomerIdAsync(customer.Id, cancellationToken);
        var existing = subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, command.ProductHandle, StringComparison.OrdinalIgnoreCase) &&
            s.State is not null && LiveStates.Contains(s.State));

        if (existing is not null)
        {
            _logger.LogInformation(
                "Customer {CustomerReference} already has a live subscription {SubscriptionId} to {ProductHandle}; returning it instead of creating a duplicate.",
                command.CustomerReference, existing.Id, command.ProductHandle);
            return Map(existing);
        }

        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = command.ProductHandle,
                CustomerReference = command.CustomerReference,
                // "remittance" enrolls without capturing a card at signup (the plans are seeded with
                // no payment method required); the balance is invoiced instead of auto-charged.
                PaymentCollectionMethod = "remittance"
            }
        };

        var created = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionResponse>(
            "subscriptions.json", request, cancellationToken);

        if (created?.Subscription is null)
        {
            throw new MaxioApiException(HttpStatusCode.InternalServerError,
                new[] { "Maxio returned an empty response when creating the subscription." });
        }

        _logger.LogInformation(
            "Created subscription {SubscriptionId} for customer {CustomerReference} on plan {ProductHandle}.",
            created.Subscription.Id, command.CustomerReference, command.ProductHandle);

        return Map(created.Subscription);
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(customerReference);

        var customer = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await ListSubscriptionsByCustomerIdAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(command.CustomerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = command.FirstName,
                LastName = command.LastName,
                Email = command.Email,
                Reference = command.CustomerReference
            }
        };

        try
        {
            var created = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerResponse>("customers.json", request, cancellationToken);
            if (created?.Customer is not null)
            {
                _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}.",
                    created.Customer.Id, command.CustomerReference);
                return created.Customer;
            }
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // The reference is unique per site; a 422 here means a concurrent request created the
            // customer first. Re-read it so the double-click never yields two customers.
            var raced = await FindCustomerByReferenceAsync(command.CustomerReference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }
            throw;
        }

        throw new MaxioApiException(HttpStatusCode.InternalServerError,
            new[] { "Maxio returned an empty response when creating the customer." });
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await GetAsync<MaxioCustomerResponse>(url, cancellationToken, allowNotFound: true);
        return response?.Customer;
    }

    private async Task<List<MaxioSubscription>> ListSubscriptionsByCustomerIdAsync(long customerId, CancellationToken cancellationToken)
    {
        var url = $"subscriptions.json?customer_id={customerId}&per_page=200";
        var responses = await GetAsync<List<MaxioSubscriptionResponse>>(url, cancellationToken) ?? new List<MaxioSubscriptionResponse>();
        return responses.Select(r => r.Subscription).Where(s => s is not null).Select(s => s!).ToList();
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
        {
            return default;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string url, TRequest body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(url, body, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        IReadOnlyList<string> errors;
        try
        {
            var errorBody = await response.Content.ReadFromJsonAsync<MaxioErrorListResponse>(cancellationToken: cancellationToken);
            errors = errorBody?.Errors is { Count: > 0 }
                ? errorBody.Errors
                : new[] { response.ReasonPhrase ?? "Unknown Maxio API error." };
        }
        catch (Exception)
        {
            errors = new[] { response.ReasonPhrase ?? "Unknown Maxio API error." };
        }

        throw new MaxioApiException(response.StatusCode, errors);
    }

    private static ShopperSubscription Map(MaxioSubscription subscription)
    {
        return new ShopperSubscription
        {
            SubscriptionId = subscription.Id,
            State = subscription.State ?? string.Empty,
            CustomerReference = subscription.Customer?.Reference ?? string.Empty,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            ProductPriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
            Interval = subscription.Product?.Interval ?? 0,
            IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
            ActivatedAt = subscription.ActivatedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingAt = subscription.NextAssessmentAt
        };
    }
}
