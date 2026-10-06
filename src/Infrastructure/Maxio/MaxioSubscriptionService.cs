using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing as the system of record.
/// Idempotency contract:
///  - one Maxio customer per eShopOnWeb user, keyed by the customer reference
///    (the ASP.NET Identity user id);
///  - one subscription per (user, plan), keyed by the subscription reference
///    "{userId}:{planHandle}".
/// Both keys are resolved through the API before creating anything, so repeated
/// calls (e.g. double-clicks) never create duplicates.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const string PlansCacheKeyPrefix = "maxio:plans:";
    private static readonly TimeSpan PlansCacheDuration = TimeSpan.FromSeconds(60);

    private readonly IMaxioClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IMaxioClient client, IOptions<MaxioOptions> options, IMemoryCache cache, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = RequireFamilyHandle();
        var products = await _cache.GetOrCreateAsync(PlansCacheKeyPrefix + familyHandle, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PlansCacheDuration;
            return await _client.ListFamilyProductsAsync(familyHandle, cancellationToken);
        });

        return products?
            .Where(p => p.ArchivedAt is null)
            .Select(MapPlan)
            .ToList()
            ?? new List<SubscriptionPlan>();
    }

    public async Task<UserSubscription> SubscribeAsync(SubscriptionUserData user, string planHandle, CancellationToken cancellationToken = default)
    {
        if (user is null || string.IsNullOrWhiteSpace(user.UserId) || string.IsNullOrWhiteSpace(user.Email))
        {
            throw new ArgumentException("A user with an id and email is required to subscribe.", nameof(user));
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var plan = (await ListPlansAsync(cancellationToken))
            .FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var customer = await EnsureCustomerAsync(user, cancellationToken);

        var reference = BuildSubscriptionReference(user.UserId, plan.Handle);
        var existing = await FindSubscriptionByReferenceAsync(customer.Id, reference, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("User {UserId} already holds subscription {SubscriptionId} for plan {PlanHandle}; returning it.",
                user.UserId, existing.Id, plan.Handle);
            return existing;
        }

        try
        {
            var created = await _client.CreateSubscriptionAsync(new MaxioCreateSubscription
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                Reference = reference,
                PaymentCollectionMethod = "remittance"
            }, cancellationToken);

            _logger.LogInformation("User {UserId} subscribed to plan {PlanHandle}: Maxio subscription {SubscriptionId} in state {State}.",
                user.UserId, plan.Handle, created.Id, created.State);
            return Map(created);
        }
        catch (MaxioApiException ex) when (ex.IsConflict)
        {
            // Concurrent double-click: Maxio enforces unique subscription references,
            // so a 422 on our deterministic reference means the duplicate won the race.
            var raced = await FindSubscriptionByReferenceAsync(customer.Id, reference, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation("Concurrent subscribe resolved idempotently for user {UserId}: subscription {SubscriptionId}.",
                    user.UserId, raced.Id);
                return raced;
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<UserSubscription>> ListUserSubscriptionsAsync(SubscriptionUserData user, CancellationToken cancellationToken = default)
    {
        if (user is null || string.IsNullOrWhiteSpace(user.UserId))
        {
            return new List<UserSubscription>();
        }

        var customer = await _client.FindCustomerByReferenceAsync(user.UserId, cancellationToken);
        if (customer is null)
        {
            return new List<UserSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriptionUserData user, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(user.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(user.Email, user.UserName);
        try
        {
            var created = await _client.CreateCustomerAsync(new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = user.Email,
                Organization = "eShopOnWeb",
                Reference = user.UserId
            }, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} (reference {Reference}) for user {UserId}.",
                created.Id, user.UserId, user.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsConflict)
        {
            // Concurrent double-click: the customer reference is unique in Maxio,
            // so a 422 means the duplicate creation won the race — re-read it.
            var raced = await _client.FindCustomerByReferenceAsync(user.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<UserSubscription?> FindSubscriptionByReferenceAsync(long customerId, string reference, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions
            .Select(Map)
            .FirstOrDefault(s => string.Equals(s.Reference, reference, StringComparison.OrdinalIgnoreCase));
    }

    private string RequireFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio product family is not configured. Set 'Maxio:ProductFamilyHandle' (environment variable MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }

        return _options.ProductFamilyHandle.Trim();
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product)
    {
        return new SubscriptionPlan
        {
            Handle = product.Handle ?? product.Id.ToString(),
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit,
            RequireCreditCard = product.RequireCreditCard,
            ProductFamilyHandle = product.ProductFamily?.Handle
        };
    }

    private static UserSubscription Map(MaxioSubscription subscription)
    {
        return new UserSubscription
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            State = subscription.State ?? string.Empty,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents,
            Currency = subscription.Currency,
            BalanceInCents = subscription.BalanceInCents,
            NextBillingDateUtc = subscription.CurrentPeriodEndsAt,
            NextAssessmentAtUtc = subscription.NextAssessmentAt,
            ActivatedAtUtc = subscription.ActivatedAt,
            ProductFamilyHandle = subscription.Product?.ProductFamily?.Handle
        };
    }

    private static string BuildSubscriptionReference(string userId, string planHandle)
    {
        return $"{userId}:{planHandle}".ToLowerInvariant();
    }

    private static (string FirstName, string LastName) SplitName(string email, string userName)
    {
        // Maxio requires a non-blank first and last name; derive them from the
        // email (which equals the username in eShopOnWeb) deterministically.
        var source = !string.IsNullOrWhiteSpace(email) ? email : userName;
        var localPart = source.Contains('@') ? source.Split('@')[0] : source;
        var tokens = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 0)
            .Select(t => Capitalize(t))
            .ToList();

        var firstName = tokens.FirstOrDefault() ?? "eShop";
        var lastName = tokens.Count > 1 ? string.Join(" ", tokens.Skip(1)) : "Subscriber";
        return (firstName, lastName);
    }

    private static string Capitalize(string value)
    {
        return string.Create(value.Length, value, (span, source) =>
        {
            source.AsSpan().CopyTo(span);
            span[0] = char.ToUpperInvariant(span[0]);
        });
    }
}