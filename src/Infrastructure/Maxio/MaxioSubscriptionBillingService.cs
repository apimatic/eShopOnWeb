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
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Talks to Maxio Advanced Billing over the endpoints documented in maxio-spec/openapi.yaml.
/// Maxio is the system of record: this service keeps no local state of its own, so every call
/// reflects Maxio's live data and repeated calls (e.g. a double-clicked "Subscribe") are safe.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> TerminalSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionBillingService(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var url = $"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new SubscriptionProviderException(
                $"Maxio product family '{_options.ProductFamilyHandle}' was not found. Check the Maxio:ProductFamilyHandle configuration value.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            ThrowUnexpected(response, body, "listing subscription plans");
        }

        var wrappers = System.Text.Json.JsonSerializer.Deserialize<List<MaxioProductWrapper>>(body) ?? new();
        return wrappers
            .Where(w => w.Product is not null)
            .Select(w => ToSubscriptionPlan(w.Product!))
            .ToList();
    }

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken = default)
    {
        var customerId = await GetOrCreateCustomerIdAsync(customerReference, customerEmail, cancellationToken);

        var existingLive = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
        if (existingLive is not null)
        {
            return new SubscriptionEnrollmentResult(existingLive, AlreadyEnrolled: true);
        }

        var requestBody = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionAttributes
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
            },
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", requestBody, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.Created)
        {
            var wrapper = System.Text.Json.JsonSerializer.Deserialize<MaxioSubscriptionWrapper>(body);
            if (wrapper?.Subscription is null)
            {
                throw new SubscriptionProviderException("Maxio returned an empty subscription on create.");
            }

            return new SubscriptionEnrollmentResult(ToCustomerSubscription(wrapper.Subscription), AlreadyEnrolled: false);
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request (e.g. a double-click) may have created the subscription between
            // our check above and this call. Re-check before surfacing the error to the caller.
            var raceWinner = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
            if (raceWinner is not null)
            {
                return new SubscriptionEnrollmentResult(raceWinner, AlreadyEnrolled: true);
            }

            throw new SubscriptionValidationException(MaxioErrorParser.ParseErrors(body));
        }

        ThrowUnexpected(response, body, "creating a subscription");
        throw new SubscriptionProviderException("unreachable");
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customerId = await FindCustomerIdAsync(customerReference, cancellationToken);
        if (customerId is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await ListSubscriptionsByCustomerIdAsync(customerId.Value, cancellationToken);
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ListSubscriptionsByCustomerIdAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            !TerminalSubscriptionStates.Contains(s.State));
    }

    private async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsByCustomerIdAsync(long customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            ThrowUnexpected(response, body, "listing customer subscriptions");
        }

        var wrappers = System.Text.Json.JsonSerializer.Deserialize<List<MaxioSubscriptionWrapper>>(body) ?? new();
        return wrappers
            .Where(w => w.Subscription is not null)
            .Select(w => ToCustomerSubscription(w.Subscription!))
            .ToList();
    }

    private async Task<long?> FindCustomerIdAsync(string reference, CancellationToken cancellationToken)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            ThrowUnexpected(response, body, "looking up a customer");
        }

        var wrapper = System.Text.Json.JsonSerializer.Deserialize<MaxioCustomerWrapper>(body);
        return wrapper?.Customer?.Id;
    }

    private async Task<long> GetOrCreateCustomerIdAsync(string reference, string email, CancellationToken cancellationToken)
    {
        var existingId = await FindCustomerIdAsync(reference, cancellationToken);
        if (existingId.HasValue)
        {
            return existingId.Value;
        }

        var (firstName, lastName) = DeriveNameFromEmail(email);
        var requestBody = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference,
            },
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", requestBody, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK)
        {
            var wrapper = System.Text.Json.JsonSerializer.Deserialize<MaxioCustomerWrapper>(body);
            if (wrapper?.Customer is null)
            {
                throw new SubscriptionProviderException("Maxio returned an empty customer on create.");
            }

            return wrapper.Customer.Id;
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request (e.g. a double-click) may have created the customer for this
            // reference between our lookup and this call - Maxio enforces reference uniqueness,
            // so recover by re-fetching rather than failing the request.
            var raceWinner = await FindCustomerIdAsync(reference, cancellationToken);
            if (raceWinner.HasValue)
            {
                return raceWinner.Value;
            }

            throw new SubscriptionValidationException(MaxioErrorParser.ParseErrors(body));
        }

        ThrowUnexpected(response, body, "creating a customer");
        throw new SubscriptionProviderException("unreachable");
    }

    private static (string FirstName, string LastName) DeriveNameFromEmail(string email)
    {
        // ApplicationUser only carries a username/email - there is no first/last name anywhere
        // upstream - so a readable name is derived from the local part of the address rather than
        // sending an empty string, which Maxio's Create Customer endpoint rejects as required.
        var localPart = email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

        var firstName = parts.Length > 0 ? Capitalize(parts[0]) : "eShopOnWeb";
        var lastName = parts.Length > 1 ? Capitalize(parts[^1]) : "Subscriber";
        return (firstName, lastName);
    }

    private static SubscriptionPlan ToSubscriptionPlan(MaxioProduct product) => new(
        product.Id,
        product.Handle,
        product.Name,
        product.Description,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit,
        product.RequireCreditCard);

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.State,
        subscription.Product?.Handle ?? string.Empty,
        subscription.Product?.Name ?? string.Empty,
        subscription.ProductPriceInCents > 0 ? subscription.ProductPriceInCents : subscription.Product?.PriceInCents ?? 0,
        subscription.CreatedAt,
        subscription.CurrentPeriodEndsAt,
        subscription.NextAssessmentAt);

    private static void ThrowUnexpected(HttpResponseMessage response, string body, string action)
    {
        var errors = MaxioErrorParser.ParseErrors(body);
        var detail = errors.Count > 0 ? string.Join(" ", errors) : body;
        throw new SubscriptionProviderException($"Maxio request failed while {action} (HTTP {(int)response.StatusCode}): {detail}");
    }
}
