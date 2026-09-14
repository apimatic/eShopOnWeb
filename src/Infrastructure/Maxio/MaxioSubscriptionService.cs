using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription enrollment backed by Maxio Advanced Billing. The user's
/// eShopOnWeb username is stored as the Maxio customer reference, and every
/// subscription gets a deterministic reference derived from the username and
/// plan handle, which makes both customer creation and enrollment idempotent.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const string SubscriptionReferencePrefix = "eshopweb";

    private readonly IMaxioApiClient _apiClient;
    private readonly MaxioOptions _options;

    // Serializes enrollment per user within this process so a double-click
    // cannot race two subscription creations past the idempotency lookup.
    private static readonly ConcurrentDictionary<string, System.Threading.SemaphoreSlim> _userLocks = new(StringComparer.OrdinalIgnoreCase);

    public MaxioSubscriptionService(IMaxioApiClient apiClient, IOptions<MaxioOptions> options)
    {
        _apiClient = apiClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync()
    {
        var products = await _apiClient.ListProductsAsync();

        return products
            .Where(p => p.ArchivedAt is null)
            .Where(p => string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscriptionSummary> SubscribeAsync(string username, string planHandle)
    {
        var semaphore = _userLocks.GetOrAdd(username, _ => new System.Threading.SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        try
        {
            var plans = await ListPlansAsync();
            if (!plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase)))
            {
                throw new UnknownSubscriptionPlanException(planHandle);
            }

            var reference = BuildSubscriptionReference(username, planHandle);

            var existing = await _apiClient.FindSubscriptionByReferenceAsync(reference);
            if (existing is not null)
            {
                return MapSubscription(existing);
            }

            var customer = await EnsureCustomerAsync(username);
            var created = await _apiClient.CreateSubscriptionAsync(new MaxioCreateSubscriptionBody
            {
                ProductHandle = planHandle,
                CustomerId = customer.Id,
                Reference = reference
            });

            return MapSubscription(created);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetMySubscriptionsAsync(string username)
    {
        var customer = await _apiClient.FindCustomerByReferenceAsync(username);
        if (customer is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = await _apiClient.ListCustomerSubscriptionsAsync(customer.Id);
        return subscriptions.Select(MapSubscription).ToList();
    }

    /// <summary>
    /// Returns the Maxio customer for the user, creating it on first use.
    /// The lookup by reference makes this idempotent across requests and restarts.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(string username)
    {
        var existing = await _apiClient.FindCustomerByReferenceAsync(username);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveCustomerNames(username);
        try
        {
            return await _apiClient.CreateCustomerAsync(new MaxioCreateCustomerBody
            {
                Reference = username,
                FirstName = firstName,
                LastName = lastName,
                Email = username
            });
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Another process may have created the customer concurrently.
            var concurrent = await _apiClient.FindCustomerByReferenceAsync(username);
            if (concurrent is not null)
            {
                return concurrent;
            }

            throw;
        }
    }

    private string BuildSubscriptionReference(string username, string planHandle)
    {
        return $"{SubscriptionReferencePrefix}:{username}:{planHandle}";
    }

    private static (string FirstName, string LastName) DeriveCustomerNames(string username)
    {
        var localPart = username.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = Capitalize(parts.FirstOrDefault() ?? "eShop");
        var lastName = Capitalize(parts.Skip(1).FirstOrDefault() ?? "Customer");
        return (firstName, lastName);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpper(value[0], CultureInfo.InvariantCulture) + value.Substring(1);
    }

    private static SubscriptionPlanInfo MapPlan(MaxioProduct product)
    {
        return new SubscriptionPlanInfo
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? product.Handle ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents ?? 0,
            BillingInterval = product.Interval ?? 1,
            BillingIntervalUnit = product.IntervalUnit ?? "month",
            ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty
        };
    }

    private static SubscriptionSummary MapSubscription(MaxioSubscription subscription)
    {
        return new SubscriptionSummary
        {
            BillingSubscriptionId = subscription.Id,
            State = subscription.State ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
            // Per the spec, current_period_ends_at is when the next regularly
            // scheduled charge will occur.
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            CreatedAt = subscription.CreatedAt ?? DateTimeOffset.UtcNow.UtcDateTime
        };
    }
}
