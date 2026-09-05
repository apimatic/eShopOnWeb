using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Talks to the Maxio Advanced Billing REST API (Basic auth over HTTPS) to list plans and enroll
/// eShopOnWeb users into recurring subscriptions. The Maxio customer "reference" is always the
/// eShopOnWeb buyer id (the authenticated user's name/email), which is what makes customer and
/// subscription creation idempotent across retries/double-clicks.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> TerminalSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionBillingService(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await GetActiveProductsAsync(cancellationToken);
        return products
            .Select(ToPlanDto)
            .OrderBy(p => p.PriceAmount)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string buyerId, string email, string? firstName, string? lastName, string planHandle, CancellationToken cancellationToken = default)
    {
        var product = (await GetActiveProductsAsync(cancellationToken))
            .FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (product is null)
        {
            throw new UnknownSubscriptionPlanException(planHandle);
        }

        var customerId = await GetOrCreateCustomerAsync(buyerId, email, firstName, lastName, cancellationToken);

        var existing = await FindLiveSubscriptionAsync(customerId, product.Handle!, cancellationToken);
        if (existing is not null)
        {
            return ToSubscriptionDto(existing);
        }

        var requestBody = new CreateSubscriptionRequestBody
        {
            Subscription = new CreateSubscriptionAttributes
            {
                ProductHandle = product.Handle!,
                CustomerId = customerId,
                UniquenessToken = BuildUniquenessToken(buyerId, product.Handle!)
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", requestBody, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // Duplicate submission of the same uniqueness_token: an in-flight or prior request already
            // created this subscription (e.g. a double-click). Return the subscription it created.
            var subscriptionFromRace = await FindLiveSubscriptionAsync(customerId, product.Handle!, cancellationToken);
            if (subscriptionFromRace is not null)
            {
                return ToSubscriptionDto(subscriptionFromRace);
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await BuildApiExceptionAsync(response, cancellationToken);
        }

        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(JsonOptions, cancellationToken);
        return ToSubscriptionDto(envelope!.Subscription);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerByReferenceAsync(buyerId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToSubscriptionDto).ToList();
    }

    private async Task<List<MaxioProduct>> GetActiveProductsAsync(CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildApiExceptionAsync(response, cancellationToken);
        }

        var items = await response.Content.ReadFromJsonAsync<List<MaxioProductListItem>>(JsonOptions, cancellationToken);
        return items?
            .Select(i => i.Product)
            .Where(p => p.ArchivedAt is null)
            .ToList() ?? new List<MaxioProduct>();
    }

    private async Task<int> GetOrCreateCustomerAsync(string reference, string email, string? firstName, string? lastName, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var (resolvedFirstName, resolvedLastName) = ResolveName(email, firstName, lastName);
        var requestBody = new CreateCustomerRequestBody
        {
            Customer = new CreateCustomerAttributes
            {
                FirstName = resolvedFirstName,
                LastName = resolvedLastName,
                Email = email,
                Reference = reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", requestBody, JsonOptions, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
            return envelope!.Customer.Id;
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio allows only one customer per reference. A 422 here most likely means a concurrent
            // request (e.g. a double-click) already created it - look it up instead of failing.
            var racedCustomer = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (racedCustomer is not null)
            {
                return racedCustomer.Id;
            }
        }

        throw await BuildApiExceptionAsync(response, cancellationToken);
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await BuildApiExceptionAsync(response, cancellationToken);
        }

        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer;
    }

    private async Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildApiExceptionAsync(response, cancellationToken);
        }

        var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionEnvelope>>(JsonOptions, cancellationToken);
        return envelopes?.Select(e => e.Subscription).ToList() ?? new List<MaxioSubscription>();
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await GetCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
            !TerminalSubscriptionStates.Contains(s.State));
    }

    private static (string FirstName, string LastName) ResolveName(string email, string? firstName, string? lastName)
    {
        if (!string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(lastName))
        {
            return (firstName!, lastName!);
        }

        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        return (
            string.IsNullOrWhiteSpace(firstName) ? localPart : firstName!,
            string.IsNullOrWhiteSpace(lastName) ? "Customer" : lastName!);
    }

    private static string BuildUniquenessToken(string buyerId, string planHandle)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"eshoponweb-subscribe:{buyerId}:{planHandle}"));
        return new Guid(hash[..16]).ToString("N");
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Description = product.Description,
        PriceAmount = product.PriceInCents / 100m,
        IntervalCount = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription) => new()
    {
        MaxioSubscriptionId = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceAmount = (subscription.Product?.PriceInCents ?? 0) / 100m,
        State = subscription.State,
        NextBillingAt = subscription.NextAssessmentAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt
    };

    private static async Task<MaxioApiException> BuildApiExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new MaxioApiException($"Maxio API request failed ({(int)response.StatusCode} {response.StatusCode}): {body}");
    }
}
