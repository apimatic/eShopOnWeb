using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Maxio.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <inheritdoc />
public class SubscriptionService : ISubscriptionService
{
    // Sub-states that mean "this subscription is still live". Everything else
    // (canceled, expired, trial_ended, failed_to_create, ...) is terminal.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "trial_ended", "failed_to_create"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMaxioApiClient _maxio;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        UserManager<ApplicationUser> userManager,
        IMaxioApiClient maxio,
        IOptionsMonitor<MaxioOptions> options,
        ILogger<SubscriptionService> logger)
    {
        _userManager = userManager;
        _maxio = maxio;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var familyHandle = ProductFamilyHandle();
        var products = await _maxio.ListProductsInFamilyAsync(familyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToPlanDto)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return new SubscribeResult { Failure = SubscribeFailureKind.UserNotFound };
        }

        // Serialize subscribe attempts per user so a double-click can never create two
        // Maxio customers or two subscriptions for the same user + plan.
        var gate = Locks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(user, productHandle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<MySubscriptionsResult> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return new MySubscriptionsResult { UserFound = false };
        }

        var customer = await _maxio.FindCustomerByReferenceAsync((user.Email ?? user.UserName)!.Trim().ToLowerInvariant(), cancellationToken);
        if (customer is null)
        {
            // No Maxio customer yet => nothing to list.
            return new MySubscriptionsResult { UserFound = true };
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var ordered = subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(ToSubscriptionDto)
            .ToList();

        return new MySubscriptionsResult { UserFound = true, Subscriptions = ordered };
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(ApplicationUser user, string productHandle, CancellationToken cancellationToken)
    {
        var familyHandle = ProductFamilyHandle();

        var familyProducts = await _maxio.ListProductsInFamilyAsync(familyHandle, cancellationToken);
        var plan = familyProducts.FirstOrDefault(p => !p.ArchivedAt.HasValue &&
                                                      string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            return new SubscribeResult
            {
                Failure = SubscribeFailureKind.PlanNotFound,
                FailureDetail = $"No subscribable plan with handle '{productHandle}' was found in the configured Maxio product family."
            };
        }

        var customer = await EnsureCustomerAsync(user, cancellationToken);

        var existing = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
        if (existing is not null)
        {
            return new SubscribeResult { Subscription = ToSubscriptionDto(existing), Created = false };
        }

        try
        {
            // No trial, no stored payment method => create the subscription so the first billing
            // lands on the natural renewal date and no payment is attempted at signup.
            var nextBillingAt = ComputeFirstBillingAt(plan.Interval, plan.IntervalUnit);
            var created = await _maxio.CreateSubscriptionAsync(plan.Handle!, customer.Id, nextBillingAt, cancellationToken);
            return new SubscribeResult { Subscription = ToSubscriptionDto(created), Created = true };
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request (or an earlier failed attempt) may have created the
            // subscription between our check and this call. Re-scan and treat it as a hit.
            var raced = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation(ex, "Subscription creation for user {UserId} plan {PlanHandle} raced; returning the existing Maxio subscription {SubscriptionId}.",
                    user.Id, productHandle, raced.Id);
                return new SubscribeResult { Subscription = ToSubscriptionDto(raced), Created = false };
            }

            throw;
        }
    }

    private async Task<Customer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        // The Maxio customer reference is the eShopOnWeb login (email). Emails are unique per
        // user and stable across restarts, which makes customer lookup/subscription idempotency
        // hold even when local storage is reset (e.g. the in-memory provider used in demos).
        var reference = (user.Email ?? user.UserName)!.Trim().ToLowerInvariant();
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveCustomerName(reference);
        var attributes = new CustomerAttributes
        {
            FirstName = firstName,
            LastName = lastName,
            Email = reference,
            Reference = reference
        };

        try
        {
            var created = await _maxio.CreateCustomerAsync(attributes, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for eShopOnWeb user {Email} (reference {Reference}).",
                created.Id, reference, reference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Another request/process created the customer for the same reference first.
            var raced = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<Subscription?> FindLiveSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            !TerminalStates.Contains(s.State ?? string.Empty) &&
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private string ProductFamilyHandle()
    {
        var handle = _options.CurrentValue.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Set 'Maxio:ProductFamilyHandle' (for example via .NET user-secrets or environment configuration).");
        }

        return handle;
    }

    // ------------------------------------------------------------------
    // Mapping helpers
    // ------------------------------------------------------------------

    private static SubscriptionPlanDto ToPlanDto(Product product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard,
        PricePointName = product.ProductPricePointName,
        PricePointHandle = product.ProductPricePointHandle
    };

    private static SubscriptionDto ToSubscriptionDto(Subscription subscription) => new()
    {
        Id = subscription.Id,
        CustomerId = subscription.Customer?.Id ?? 0,
        State = subscription.State ?? string.Empty,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        ProductPriceInCents = subscription.ProductPriceInCents,
        ProductPricePointId = subscription.ProductPricePointId,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
        Currency = subscription.Currency,
        Reference = subscription.Reference
    };

    private static (string FirstName, string LastName) DeriveCustomerName(string? email)
    {
        var fallback = (FirstName: "eShop", LastName: "User");
        if (string.IsNullOrWhiteSpace(email) || email.IndexOf('@') <= 0)
        {
            return fallback;
        }

        var local = email.Substring(0, email.IndexOf('@'));
        var domain = email.Substring(email.IndexOf('@') + 1);

        var tokens = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var words = tokens.Select(Capitalize).ToArray();
        if (words.Length == 0)
        {
            return fallback;
        }

        if (words.Length == 1)
        {
            // No separable name in the local part; fall back to the first label of the domain.
            var domainLabel = domain.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return (words[0], string.IsNullOrWhiteSpace(domainLabel) ? fallback.LastName : Capitalize(domainLabel));
        }

        return (words[0], string.Join(" ", words.Skip(1)));
    }

    private static string Capitalize(string value) =>
        value.Length > 0
            ? char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant()
            : value;

    private static DateTimeOffset ComputeFirstBillingAt(int? interval, string? intervalUnit)
    {
        var count = interval is > 0 ? interval.Value : 1;
        var now = DateTimeOffset.UtcNow;
        return string.Equals(intervalUnit, "day", StringComparison.OrdinalIgnoreCase)
            ? now.AddDays(count)
            : now.AddMonths(count);
    }
}
