using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken);
}

public sealed class SubscriptionService : ISubscriptionService
{
    private const string CustomerReferencePrefix = "eshop-user:";
    private const string SubscriptionReferencePrefix = "eshop-sub:";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly AppIdentityDbContext _identityDbContext;
    private readonly IMaxioBillingClient _maxioClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        AppIdentityDbContext identityDbContext,
        IMaxioBillingClient maxioClient,
        IOptions<MaxioOptions> options,
        ILogger<SubscriptionService> logger)
    {
        _identityDbContext = identityDbContext;
        _maxioClient = maxioClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxioClient.ListPlansAsync(cancellationToken);
        return products.Select(product => new SubscriptionPlanDto
        {
            Handle = product.Handle,
            Name = product.Name,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit,
            TrialInterval = product.TrialInterval,
            TrialIntervalUnit = product.TrialIntervalUnit,
            RequiresCreditCard = product.RequireCreditCard,
            Taxable = product.Taxable
        }).ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionPlanNotFoundException("A subscription plan handle is required.");
        }

        var gate = UserLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var products = await _maxioClient.ListPlansAsync(cancellationToken);
            var product = products.FirstOrDefault(candidate =>
                string.Equals(candidate.Handle, productHandle.Trim(), StringComparison.OrdinalIgnoreCase));
            if (product is null)
            {
                throw new SubscriptionPlanNotFoundException("The requested subscription plan was not found.");
            }

            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var subscriptionReference = CreateSubscriptionReference(user.Id, product.Handle);
            var existingLink = await _identityDbContext.MaxioSubscriptionLinks
                .SingleOrDefaultAsync(link => link.UserId == user.Id && link.ProductHandle == product.Handle, cancellationToken);

            if (existingLink is not null)
            {
                var existingSubscription = await _maxioClient.GetSubscriptionAsync(existingLink.MaxioSubscriptionId, cancellationToken);
                return ToSubscriptionDto(existingSubscription, product);
            }

            // A successful Maxio request can be followed by a local database/network failure.
            // Reconcile by the deterministic reference before attempting another create.
            var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var recoveredSubscription = existingSubscriptions.FirstOrDefault(subscription =>
                string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal));
            if (recoveredSubscription is not null)
            {
                await SaveSubscriptionLinkAsync(user.Id, customer.Id, product.Handle, subscriptionReference, recoveredSubscription.Id, cancellationToken);
                return ToSubscriptionDto(recoveredSubscription, product);
            }

            MaxioSubscription createdSubscription;
            try
            {
                createdSubscription = await _maxioClient.CreateSubscriptionAsync(new MaxioCreateSubscription
                {
                    ProductHandle = product.Handle,
                    CustomerReference = customer.Reference,
                    PaymentCollectionMethod = "remittance",
                    Reference = subscriptionReference,
                    UniquenessToken = CreateUniquenessToken(user.Id, product.Handle)
                }, cancellationToken);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
            {
                // Maxio returns 409 when the same uniqueness_token is replayed. Read the
                // customer's subscriptions to recover the original result.
                var replayedSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
                var replayedSubscription = replayedSubscriptions.FirstOrDefault(subscription =>
                    string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal))
                    ?? throw new MaxioApiException("Maxio reported a duplicate subscription, but the original could not be recovered.", HttpStatusCode.Conflict);
                createdSubscription = replayedSubscription;
            }

            await SaveSubscriptionLinkAsync(
                user.Id,
                customer.Id,
                product.Handle,
                subscriptionReference,
                createdSubscription.Id,
                cancellationToken);

            return ToSubscriptionDto(createdSubscription, product);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customerLink = await _identityDbContext.MaxioCustomerLinks
            .SingleOrDefaultAsync(link => link.UserId == user.Id, cancellationToken);

        MaxioCustomer? customer = customerLink is null
            ? await _maxioClient.FindCustomerByReferenceAsync(CreateCustomerReference(user.Id), cancellationToken)
            : new MaxioCustomer { Id = customerLink.MaxioCustomerId, Reference = customerLink.CustomerReference };

        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var plans = await _maxioClient.ListPlansAsync(cancellationToken);
        var plansByHandle = plans.ToDictionary(plan => plan.Handle, StringComparer.OrdinalIgnoreCase);
        var linkedSubscriptionIds = await _identityDbContext.MaxioSubscriptionLinks
            .Where(link => link.UserId == user.Id)
            .Select(link => link.MaxioSubscriptionId)
            .ToListAsync(cancellationToken);
        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .Where(subscription =>
                linkedSubscriptionIds.Contains(subscription.Id) ||
                (subscription.Product?.ProductFamily?.Handle is not null &&
                 string.Equals(subscription.Product.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase)))
            .Select(subscription =>
            {
                plansByHandle.TryGetValue(subscription.Product?.Handle ?? string.Empty, out var plan);
                return ToSubscriptionDto(subscription, plan);
            })
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CreateCustomerReference(user.Id);
        var existingLink = await _identityDbContext.MaxioCustomerLinks
            .SingleOrDefaultAsync(link => link.UserId == user.Id, cancellationToken);
        if (existingLink is not null)
        {
            return new MaxioCustomer { Id = existingLink.MaxioCustomerId, Reference = existingLink.CustomerReference };
        }

        var customer = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            try
            {
                customer = await _maxioClient.CreateCustomerAsync(new MaxioCreateCustomer
                {
                    FirstName = "eShop",
                    LastName = "Shopper",
                    Email = user.Email ?? user.UserName ?? reference,
                    Reference = reference
                }, cancellationToken);
            }
            catch (MaxioApiException exception) when ((int)exception.StatusCode is 400 or 422)
            {
                // Another app instance may have won the create race. The reference is
                // unique in Maxio, so resolving it is safe and deterministic.
                var recoveredCustomer = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
                if (recoveredCustomer is null)
                {
                    throw new MaxioApiException("Maxio rejected customer creation and the customer could not be recovered.", exception.StatusCode);
                }

                customer = recoveredCustomer;
            }
        }

        await SaveCustomerLinkAsync(user.Id, customer, cancellationToken);
        return customer;
    }

    private async Task SaveCustomerLinkAsync(string userId, MaxioCustomer customer, CancellationToken cancellationToken)
    {
        var link = new MaxioCustomerLink
        {
            UserId = userId,
            MaxioCustomerId = customer.Id,
            CustomerReference = string.IsNullOrWhiteSpace(customer.Reference)
                ? CreateCustomerReference(userId)
                : customer.Reference
        };

        _identityDbContext.MaxioCustomerLinks.Add(link);
        try
        {
            await _identityDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _identityDbContext.Entry(link).State = EntityState.Detached;
            var existing = await _identityDbContext.MaxioCustomerLinks
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);
            if (existing is null)
            {
                throw;
            }
        }
    }

    private async Task SaveSubscriptionLinkAsync(
        string userId,
        long customerId,
        string productHandle,
        string subscriptionReference,
        long subscriptionId,
        CancellationToken cancellationToken)
    {
        var link = new MaxioSubscriptionLink
        {
            UserId = userId,
            MaxioCustomerId = customerId,
            MaxioSubscriptionId = subscriptionId,
            ProductHandle = productHandle,
            SubscriptionReference = subscriptionReference
        };

        _identityDbContext.MaxioSubscriptionLinks.Add(link);
        try
        {
            await _identityDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _identityDbContext.Entry(link).State = EntityState.Detached;
            var existing = await _identityDbContext.MaxioSubscriptionLinks
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId && candidate.ProductHandle == productHandle, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            _logger.LogInformation("Recovered an existing Maxio subscription link for user {UserId} and plan {ProductHandle}.", userId, productHandle);
        }
    }

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription, MaxioProduct? plan)
    {
        var product = subscription.Product ?? plan;
        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id,
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents ?? product?.PriceInCents,
            Interval = product?.Interval,
            IntervalUnit = product?.IntervalUnit,
            State = subscription.State,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
        };
    }

    private static string CreateCustomerReference(string userId) => CustomerReferencePrefix + userId;

    private static string CreateSubscriptionReference(string userId, string productHandle) =>
        SubscriptionReferencePrefix + ComputeHash(userId + ":" + productHandle).Substring(0, 48);

    private static string CreateUniquenessToken(string userId, string productHandle) =>
        ComputeHash("subscription:" + userId + ":" + productHandle);

    private static string ComputeHash(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string message) : base(message) { }
}

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public int? TrialInterval { get; init; }
    public string? TrialIntervalUnit { get; init; }
    public bool RequiresCreditCard { get; init; }
    public bool Taxable { get; init; }
}

public sealed class SubscriptionDto
{
    public long SubscriptionId { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long? PriceInCents { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
}
