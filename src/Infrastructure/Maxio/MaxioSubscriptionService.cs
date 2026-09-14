using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements subscription enrollment against Maxio Advanced Billing.
/// The Maxio customer reference is the eShopOnWeb user id, which makes the
/// user-to-billing-customer mapping stable without local persistence.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "failed_to_create", "trialing", "assessing", "active", "soft_failure",
        "past_due", "suspended", "unpaid", "awaiting_signup"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);

    private readonly MaxioApiClient _apiClient;
    private readonly MaxioSettings _settings;

    public MaxioSubscriptionService(MaxioApiClient apiClient, IOptions<MaxioSettings> settings)
    {
        _apiClient = apiClient;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _apiClient.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products.Select(MapPlan).ToList();
    }

    public async Task<SubscriptionDetails> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var userLock = UserLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(userId, email, cancellationToken);

            var existingSubscriptions = await _apiClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = existingSubscriptions.FirstOrDefault(s =>
                s.Product is not null &&
                string.Equals(s.Product.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                s.State is not null &&
                LiveSubscriptionStates.Contains(s.State));

            if (existing is not null)
            {
                var result = MapSubscription(existing, userId);
                result.AlreadyExisted = true;
                return result;
            }

            var reference = BuildSubscriptionReference(userId, planHandle);
            var created = await _apiClient.CreateSubscriptionAsync(customer.Id, planHandle, reference, cancellationToken);
            return MapSubscription(created, userId);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListUserSubscriptionsAsync(string userId, string email, CancellationToken cancellationToken = default)
    {
        var customer = await EnsureCustomerAsync(userId, email, cancellationToken);
        var subscriptions = await _apiClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .Where(s => s.State is not null && LiveSubscriptionStates.Contains(s.State))
            .Select(s => MapSubscription(s, userId))
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userId, string email, CancellationToken cancellationToken)
    {
        var existing = await _apiClient.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveCustomerName(email);
        try
        {
            return await _apiClient.CreateCustomerAsync(userId, email, firstName, lastName, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // The Billing API enforces one customer per reference value; a concurrent
            // signup for the same user means the customer now exists - look it up.
            var racedCustomer = await _apiClient.FindCustomerByReferenceAsync(userId, cancellationToken);
            if (racedCustomer is not null)
            {
                return racedCustomer;
            }

            throw;
        }
    }

    private static (string FirstName, string LastName) DeriveCustomerName(string email)
    {
        var localPart = email.Split('@')[0];
        var nameParts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => new string(p.Where(char.IsLetter).ToArray()))
            .Where(p => p.Length > 0)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant())
            .ToList();

        return (
            FirstName: nameParts.Count > 0 ? nameParts[0] : "Customer",
            LastName: nameParts.Count > 1 ? string.Join(" ", nameParts.Skip(1)) : "Customer");
    }

    private static string BuildSubscriptionReference(string userId, string planHandle)
    {
        return $"eshopweb-{userId}-{planHandle}";
    }

    private static SubscriptionPlanInfo MapPlan(MaxioProduct product)
    {
        return new SubscriptionPlanInfo
        {
            Handle = product.Handle ?? product.Id.ToString(),
            Name = product.Name,
            Description = product.Description,
            Price = CentsToDecimal(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? "month"
        };
    }

    private static SubscriptionDetails MapSubscription(MaxioSubscription subscription, string customerReference)
    {
        return new SubscriptionDetails
        {
            SubscriptionId = (int)subscription.Id,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            State = subscription.State ?? string.Empty,
            Price = CentsToDecimal(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0),
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CustomerReference = subscription.Customer?.Reference ?? customerReference
        };
    }

    private static decimal CentsToDecimal(long cents)
    {
        return cents / 100m;
    }
}
