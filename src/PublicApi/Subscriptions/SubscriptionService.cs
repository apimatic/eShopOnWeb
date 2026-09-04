using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private readonly IMaxioBillingClient _maxio;
    private readonly AppIdentityDbContext _identityDb;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionService(IMaxioBillingClient maxio, AppIdentityDbContext identityDb, UserManager<ApplicationUser> userManager)
    {
        _maxio = maxio;
        _identityDb = identityDb;
        _userManager = userManager;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);
        return products.Select(ToPlan).ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string requestedPlanHandle, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByNameAsync(userName)
            ?? throw new SubscriptionUserNotFoundException();

        var products = await _maxio.ListProductsAsync(cancellationToken);
        var product = products.SingleOrDefault(item => string.Equals(item.Handle, requestedPlanHandle, StringComparison.Ordinal));
        if (product is null || string.IsNullOrWhiteSpace(product.Handle))
            throw new SubscriptionPlanNotFoundException();

        var lockKey = $"{user.Id}:{product.Handle}";
        var gate = Locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customerReference = CustomerReference(user.Id);
            var customer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (customer is null)
            {
                var (firstName, lastName) = CustomerName(user.Email ?? user.UserName ?? "shopper@example.com");
                try
                {
                    customer = await _maxio.CreateCustomerAsync(firstName, lastName, user.Email ?? user.UserName ?? string.Empty, customerReference, cancellationToken);
                }
                catch (MaxioApiException ex) when ((int)ex.StatusCode == 422)
                {
                    var existingCustomer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
                    if (existingCustomer is null)
                        throw;

                    customer = existingCustomer;
                }
            }

            var subscriptionReference = SubscriptionReference(user.Id, product.Handle);
            var subscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (subscription is null)
            {
                try
                {
                    subscription = await _maxio.CreateSubscriptionAsync(customer.Id, product.Handle, subscriptionReference, cancellationToken);
                }
                catch (MaxioApiException ex) when ((int)ex.StatusCode == 422)
                {
                    var existingSubscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                    if (existingSubscription is null)
                        throw;

                    subscription = existingSubscription;
                }
            }

            await SaveMappingAsync(user.Id, customer.Id, subscription.Id, product.Handle, subscriptionReference, cancellationToken);
            return ToSubscription(subscription, product);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByNameAsync(userName)
            ?? throw new SubscriptionUserNotFoundException();

        var customer = await _maxio.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
            return Array.Empty<SubscriptionDto>();

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var result = new List<SubscriptionDto>(subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            var product = subscription.Product ?? new MaxioProduct
            {
                Handle = subscription.Reference is null ? string.Empty : subscription.Reference,
                Name = string.Empty,
                PriceInCents = subscription.PriceInCents
            };
            result.Add(ToSubscription(subscription, product));
        }

        return result;
    }

    private async Task SaveMappingAsync(string userId, int customerId, int subscriptionId, string productHandle, string reference, CancellationToken cancellationToken)
    {
        var mapping = await _identityDb.MaxioSubscriptionMappings
            .SingleOrDefaultAsync(item => item.UserId == userId && item.ProductHandle == productHandle, cancellationToken);
        var now = DateTime.UtcNow;
        if (mapping is null)
        {
            _identityDb.MaxioSubscriptionMappings.Add(new MaxioSubscriptionMapping
            {
                UserId = userId,
                MaxioCustomerId = customerId,
                MaxioSubscriptionId = subscriptionId,
                ProductHandle = productHandle,
                SubscriptionReference = reference,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }
        else
        {
            mapping.MaxioCustomerId = customerId;
            mapping.MaxioSubscriptionId = subscriptionId;
            mapping.SubscriptionReference = reference;
            mapping.UpdatedAtUtc = now;
        }

        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionPlanDto ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Price = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static SubscriptionDto ToSubscription(MaxioSubscription subscription, MaxioProduct product) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = product.Handle ?? string.Empty,
        PlanName = product.Name,
        PriceInCents = subscription.PriceInCents != 0 ? subscription.PriceInCents : product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        Price = (subscription.PriceInCents != 0 ? subscription.PriceInCents : product.PriceInCents) / 100m,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        NextAssessmentDate = subscription.NextAssessmentAt
    };

    private static string CustomerReference(string userId) => $"eshop:user:{userId}";

    private static string SubscriptionReference(string userId, string productHandle) => $"eshop:user:{userId}:plan:{productHandle}";

    private static (string FirstName, string LastName) CustomerName(string email)
    {
        var localPart = email.Split('@', 2)[0];
        var pieces = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return pieces.Length switch
        {
            >= 2 => (TrimName(pieces[0]), TrimName(pieces[1])),
            1 => (TrimName(pieces[0]), "Customer"),
            _ => ("eShop", "Customer")
        };
    }

    private static string TrimName(string value) => value.Length > 100 ? value[..100] : value;
}

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public int? TrialInterval { get; init; }
    public string? TrialIntervalUnit { get; init; }
    public bool RequiresPaymentMethod { get; init; }
}

public sealed class SubscriptionDto
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
    public DateTimeOffset? NextAssessmentDate { get; init; }
}

public sealed class SubscriptionUserNotFoundException : Exception
{
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
}
