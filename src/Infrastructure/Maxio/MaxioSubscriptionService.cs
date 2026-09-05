using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates eShopOnWeb buyer subscriptions against Maxio Advanced Billing: ensures a
/// Maxio customer exists for the buyer (idempotent, keyed by the buyer's username as the
/// customer's external "reference"), and enrolls them into a plan without ever creating a
/// duplicate customer or subscription on retry/double-click.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly HashSet<string> TerminalSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private readonly IMaxioApiClient _client;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(IMaxioApiClient client, IOptions<MaxioOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);

        var plans = new List<MaxioPlan>();
        foreach (var product in products)
        {
            if (product.ArchivedAt.HasValue)
            {
                continue;
            }
            plans.Add(MapPlan(product));
        }
        return plans;
    }

    public async Task<MaxioSubscription> SubscribeAsync(string buyerId, string buyerEmail, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new ArgumentException("A buyer id is required.", nameof(buyerId));
        }
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var customer = await FindOrCreateCustomerAsync(buyerId, buyerEmail, cancellationToken);

        var existing = await FindNonTerminalSubscriptionForPlanAsync(customer.Id, planHandle, cancellationToken);
        if (existing != null)
        {
            return MapSubscription(existing);
        }

        var uniquenessToken = BuildUniquenessToken("subscription", buyerId, planHandle);
        try
        {
            var subscription = await _client.CreateSubscriptionAsync(new MaxioCreateSubscriptionAttributes
            {
                ProductHandle = planHandle,
                CustomerReference = buyerId
            }, uniquenessToken, cancellationToken);

            return MapSubscription(subscription);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Maxio's duplicate-prevention rejected this exact request (same uniqueness token
            // within its 60-minute window) because an earlier, otherwise-identical request -
            // most likely a double-click of the same subscribe action - already went through.
            // The winning subscription should now exist; return it instead of erroring.
            var afterConflict = await FindNonTerminalSubscriptionForPlanAsync(customer.Id, planHandle, cancellationToken);
            if (afterConflict == null)
            {
                throw;
            }
            return MapSubscription(afterConflict);
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new ArgumentException("A buyer id is required.", nameof(buyerId));
        }

        var customer = await _client.FindCustomerByReferenceAsync(buyerId, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var result = new List<MaxioSubscription>(subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            result.Add(MapSubscription(subscription));
        }
        return result;
    }

    private async Task<MaxioCustomerModel> FindOrCreateCustomerAsync(string buyerId, string buyerEmail, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(buyerId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(buyerEmail, buyerId);
        var uniquenessToken = BuildUniquenessToken("customer", buyerId);

        try
        {
            return await _client.CreateCustomerAsync(new MaxioCreateCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = buyerEmail,
                Reference = buyerId
            }, uniquenessToken, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity || ex.StatusCode == HttpStatusCode.Conflict)
        {
            // A concurrent request for the same buyer (double-click, or a retried request
            // after a timeout) may have just created this exact customer by reference -
            // Maxio enforces reference uniqueness server-side. Re-fetch rather than fail.
            var afterRace = await _client.FindCustomerByReferenceAsync(buyerId, cancellationToken);
            if (afterRace == null)
            {
                throw;
            }
            return afterRace;
        }
    }

    private async Task<MaxioSubscriptionModel?> FindNonTerminalSubscriptionForPlanAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        foreach (var subscription in subscriptions)
        {
            if (string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                !TerminalSubscriptionStates.Contains(subscription.State))
            {
                return subscription;
            }
        }
        return null;
    }

    private static string BuildUniquenessToken(params string[] parts)
    {
        var raw = string.Join(":", parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    private static (string FirstName, string LastName) SplitDisplayName(string email, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(email) ? fallback : email;
        var localPart = source.Split('@')[0];
        var segments = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length >= 2)
        {
            return (Capitalize(segments[0]), Capitalize(segments[1]));
        }

        var name = segments.Length == 1 ? segments[0] : "Subscriber";
        return (Capitalize(name), "Customer");
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }
        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static MaxioPlan MapPlan(MaxioProductModel product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresCreditCard = product.RequireCreditCard
    };

    private static MaxioSubscription MapSubscription(MaxioSubscriptionModel subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? 0,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        NextBillingAt = subscription.NextAssessmentAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt
    };
}
