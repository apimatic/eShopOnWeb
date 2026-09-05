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
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Talks to Maxio Advanced Billing over its REST API (Basic Auth: API key as username, "X" as
/// password). Maxio is the system of record for subscriptions - nothing here is cached or
/// persisted locally.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // States in which a subscription still represents a "live" enrollment in a plan, i.e. one
    // that a repeat POST /api/subscriptions for the same plan should not duplicate.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "trialing", "assessing", "active", "soft_failure", "past_due",
        "suspended", "paused", "unpaid", "awaiting_signup"
    };

    private readonly HttpClient _httpClient;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionBillingService(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _productFamilyHandle = options.Value.ProductFamilyHandle;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var handle = Uri.EscapeDataString(_productFamilyHandle);
        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(
            HttpMethod.Get, $"/product_families/handle:{handle}/products.json", body: null, cancellationToken);

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
            .Select(p => new SubscriptionPlan
            {
                Handle = p!.Handle!,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            })
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(string buyerId, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        var customer = await FindOrCreateCustomerAsync(buyerId, email, cancellationToken);

        var existing = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var live = existing.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
            && LiveStates.Contains(s.State));
        if (live is not null)
        {
            return ToCustomerSubscription(live);
        }

        var paymentCollectionMethod = await DeterminePaymentCollectionMethodAsync(cancellationToken);
        var created = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post,
            "/subscriptions.json",
            new MaxioSubscriptionCreateEnvelope
            {
                Subscription = new MaxioSubscriptionCreate
                {
                    CustomerId = customer.Id,
                    ProductHandle = planHandle,
                    PaymentCollectionMethod = paymentCollectionMethod
                }
            },
            cancellationToken);

        if (created.Subscription is null)
        {
            throw new MaxioApiException(502, "Maxio did not return a subscription.");
        }

        return ToCustomerSubscription(created.Subscription);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerByReferenceAsync(buyerId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToCustomerSubscription).ToList();
    }

    private async Task<MaxioCustomer> FindOrCreateCustomerAsync(string buyerId, string email, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(buyerId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(email);
        try
        {
            var created = await SendAsync<MaxioCustomerEnvelope>(
                HttpMethod.Post,
                "/customers.json",
                new MaxioCustomerCreateEnvelope
                {
                    Customer = new MaxioCustomerCreate
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = buyerId
                    }
                },
                cancellationToken);

            if (created.Customer is null)
            {
                throw new MaxioApiException(502, "Maxio did not return a customer.");
            }

            return created.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // A concurrent request (e.g. a double-click) may have created the customer for this
            // reference between our lookup and this create call. Re-fetch instead of failing.
            var raceWinner = await FindCustomerByReferenceAsync(buyerId, cancellationToken);
            if (raceWinner is not null)
            {
                return raceWinner;
            }

            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string buyerId, CancellationToken cancellationToken)
    {
        var reference = Uri.EscapeDataString(buyerId);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/customers/lookup.json?reference={reference}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Customer;
    }

    private async Task<string> DeterminePaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var site = await SendAsync<MaxioSiteEnvelope>(HttpMethod.Get, "/site.json", body: null, cancellationToken);
        return site.Site?.RelationshipInvoicingEnabled == true ? "remittance" : "invoice";
    }

    private async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get, $"/customers/{customerId}/subscriptions.json", body: null, cancellationToken);
        return envelopes.Select(e => e.Subscription).Where(s => s is not null).Select(s => s!).ToList();
    }

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? 0,
        State = subscription.State,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt
    };

    private static (string FirstName, string LastName) SplitDisplayName(string email)
    {
        var localPart = email.Split('@')[0];
        return string.IsNullOrWhiteSpace(localPart) ? ("eShopOnWeb", "Customer") : (localPart, "Customer");
    }

    private async Task<TResponse> SendAsync<TResponse>(HttpMethod method, string requestUri, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        if (result is null)
        {
            throw new MaxioApiException(502, $"Maxio returned an empty response for {method} {requestUri}.");
        }

        return result;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = Array.Empty<string>();
        try
        {
            var errorBody = await response.Content.ReadFromJsonAsync<MaxioErrorEnvelope>(cancellationToken: cancellationToken);
            errors = errorBody?.Errors?.Messages.ToArray() ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            // Body wasn't JSON (or didn't match the expected shape); fall through with no detail.
        }

        var message = errors.Length > 0
            ? string.Join("; ", errors)
            : $"Maxio request failed with status {(int)response.StatusCode}.";

        var mappedStatus = (int)response.StatusCode switch
        {
            404 => 404,
            422 => 422,
            401 or 403 => 502, // our own credentials are misconfigured; not the caller's fault
            _ => 502
        };

        throw new MaxioApiException(mappedStatus, message, errors);
    }
}
