using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements subscription management against Maxio Advanced Billing (the Billing API).
/// The eShopOnWeb user is linked to a Maxio customer through the customer's "reference"
/// field, which makes both customer creation and plan enrollment idempotent without
/// requiring any local persistence.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const string CustomerReferencePrefix = "eshoponweb-";
    private const int ProductsPageSize = 200;

    /// <summary>
    /// Subscription states that mean the shopper is still enrolled. Anything else
    /// (canceled, expired, trial_ended, failed_to_create, ...) allows a new signup.
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "awaiting_signup", "pending", "assessing", "trialing", "active",
        "soft_failure", "past_due", "unpaid", "suspended", "on_hold"
    };

    private readonly MaxioClient _maxioClient;
    private readonly MaxioOptions _options;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioClient maxioClient,
        IOptions<MaxioOptions> options,
        UserManager<ApplicationUser> userManager,
        ILogger<MaxioSubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _options = options.Value;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProductWire>();
        var page = 1;
        while (true)
        {
            var batch = await _maxioClient.GetAsync<List<MaxioProductResponseWire>>(
                $"product_families/handle:{_options.ProductFamilyHandle}/products.json?page={page}&per_page={ProductsPageSize}");
            if (batch is null)
            {
                if (page == 1)
                {
                    throw new MaxioApiException(
                        $"Product family '{_options.ProductFamilyHandle}' was not found in Maxio.",
                        (int)HttpStatusCode.NotFound);
                }
                break;
            }

            products.AddRange(batch.Where(w => w.Product is not null).Select(w => w.Product!));
            if (batch.Count < ProductsPageSize)
            {
                break;
            }
            page++;
        }

        return products
            .Where(p => p.ArchivedAt is null)
            .Select(ToPlan)
            .ToList();
    }

    public async Task<(SubscriptionDetails Subscription, bool Created)> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(userName)
            ?? throw new InvalidOperationException($"User '{userName}' does not exist.");

        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var customer = await EnsureCustomerAsync(user, cancellationToken);

        var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Id);
        if (existing is not null)
        {
            _logger.LogInformation("User {UserName} is already subscribed to plan {PlanHandle} (subscription {SubscriptionId}).",
                userName, plan.Handle, existing.Id);
            return (ToDetails(existing, plans), false);
        }

        var request = new MaxioCreateSubscriptionRequestWire
        {
            UniquenessToken = Guid.NewGuid().ToString("N"),
            Subscription = new MaxioCreateSubscriptionBodyWire
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id
            }
        };

        var created = await _maxioClient.PostAsync<MaxioSubscriptionResponseWire>("subscriptions.json", request);
        _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserName} on plan {PlanHandle}.",
            created.Subscription?.Id, userName, plan.Handle);

        return (ToDetails(created.Subscription!, plans), true);
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var customer = await FindCustomerByReferenceAsync(CustomerReference(user.Id));
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _maxioClient
            .GetAsync<List<MaxioSubscriptionResponseWire>>($"customers/{customer.Id}/subscriptions.json") ?? new List<MaxioSubscriptionResponseWire>();

        return subscriptions
            .Where(s => s.Subscription is not null)
            .Select(s => ToDetails(s.Subscription!, plans: null))
            .ToList();
    }

    /// <summary>
    /// Returns the Maxio customer for the given user, creating one if none exists yet.
    /// Idempotent: keyed on the customer reference derived from the local user id.
    /// </summary>
    private async Task<MaxioCustomerWire> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);

        var existing = await FindCustomerByReferenceAsync(reference);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(user);
        try
        {
            var created = await _maxioClient.PostAsync<MaxioCustomerResponseWire>(
                "customers.json",
                new MaxioCreateCustomerRequestWire
                {
                    Customer = new MaxioCreateCustomerBodyWire
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = user.Email ?? user.UserName!,
                        Reference = reference,
                        Organization = "eShopOnWeb"
                    }
                });
            return created.Customer!;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            // Either we lost a race (another request created the customer first) or a
            // previous attempt created it after our lookup. Either way the customer exists now.
            _logger.LogWarning("Creating Maxio customer for reference {Reference} failed ({Message}); falling back to lookup.",
                reference, ex.Message);
            var racedCustomer = await FindCustomerByReferenceAsync(reference)
                ?? throw ex;
            return racedCustomer;
        }
    }

    private async Task<MaxioCustomerWire?> FindCustomerByReferenceAsync(string reference)
    {
        try
        {
            return (await _maxioClient.GetAsync<MaxioCustomerResponseWire>($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}"))?.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            // Maxio answers 422 when no customer matches the reference.
            return null;
        }
    }

    private async Task<MaxioSubscriptionWire?> FindLiveSubscriptionAsync(int customerId, int productId)
    {
        var subscriptions = await _maxioClient
            .GetAsync<List<MaxioSubscriptionResponseWire>>($"customers/{customerId}/subscriptions.json") ?? new List<MaxioSubscriptionResponseWire>();

        return subscriptions
            .Select(s => s.Subscription)
            .FirstOrDefault(s => s is not null
                && (s.ProductId == productId || s.Product?.Id == productId)
                && s.State is not null
                && LiveStates.Contains(s.State));
    }

    private static string CustomerReference(string userId) => CustomerReferencePrefix + userId;

    private static SubscriptionPlan ToPlan(MaxioProductWire product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? product.Id.ToString(CultureInfo.InvariantCulture),
        Name = product.Name ?? product.Handle ?? product.Id.ToString(CultureInfo.InvariantCulture),
        Description = product.Description,
        Price = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
        RequireCreditCard = product.RequireCreditCard
    };

    private static SubscriptionDetails ToDetails(MaxioSubscriptionWire subscription, IReadOnlyList<SubscriptionPlan>? plans)
    {
        var plan = plans?.FirstOrDefault(p => p.Id == subscription.ProductId);
        return new SubscriptionDetails
        {
            Id = subscription.Id,
            PlanId = subscription.Product?.Id ?? subscription.ProductId ?? 0,
            PlanHandle = subscription.Product?.Handle ?? plan?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? plan?.Name ?? $"Plan {subscription.ProductId}",
            Price = subscription.ProductPriceInCents / 100m,
            State = subscription.State ?? string.Empty,
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            CustomerId = subscription.Customer?.Id ?? subscription.CustomerId ?? 0
        };
    }

    private static (string FirstName, string LastName) DeriveName(ApplicationUser user)
    {
        var localPart = (user.Email ?? user.UserName ?? "eShop Customer").Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstName = ToDisplayName(parts.Length > 0 ? parts[0] : "eShop");
        var lastName = ToDisplayName(parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "Customer");
        return (firstName, lastName);
    }

    private static string ToDisplayName(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
