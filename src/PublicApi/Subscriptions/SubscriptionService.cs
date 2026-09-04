using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);
    private readonly IMaxioBillingClient _maxio;
    private readonly AppIdentityDbContext _identityDb;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioOptions _options;

    public SubscriptionService(
        IMaxioBillingClient maxio,
        AppIdentityDbContext identityDb,
        UserManager<ApplicationUser> userManager,
        Microsoft.Extensions.Options.IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _identityDb = identityDb;
        _userManager = userManager;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => new SubscriptionPlanDto
            {
                Handle = product.Handle!,
                Name = product.Name ?? product.Handle!,
                Description = product.Description,
                PriceInCents = product.PriceInCents,
                Interval = product.Interval,
                IntervalUnit = product.IntervalUnit ?? string.Empty,
                PaymentMethodRequired = product.RequireCreditCard
            })
            .OrderBy(plan => plan.PriceInCents)
            .ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        var userLock = UserLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var plan = (await GetPlansAsync(cancellationToken))
                .FirstOrDefault(candidate => string.Equals(candidate.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
            if (plan is null)
            {
                throw new ArgumentException($"The subscription plan '{planHandle}' is not available.", nameof(planHandle));
            }

            var existingRecord = await _identityDb.MaxioSubscriptionRecords
                .SingleOrDefaultAsync(record => record.UserId == user.Id, cancellationToken);
            if (existingRecord is not null)
            {
                var existing = (await _maxio.ListCustomerSubscriptionsAsync(existingRecord.MaxioCustomerId, cancellationToken))
                    .FirstOrDefault(subscription => subscription.Id == existingRecord.MaxioSubscriptionId);
                if (existing is not null)
                {
                    if (!string.Equals(existingRecord.ProductHandle, planHandle, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new DuplicateException("This account already has a subscription. Plan changes are not supported by this endpoint.");
                    }

                    return ToDto(existing, existingRecord.ProductHandle);
                }
            }

            var customerReference = GetCustomerReference(user);
            var customer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken)
                ?? await _maxio.CreateCustomerAsync(
                    customerReference,
                    user.UserName ?? user.Email ?? "Shopper",
                    "Shopper",
                    user.Email ?? user.UserName ?? string.Empty,
                    cancellationToken);

            var remoteSubscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existingRemote = remoteSubscriptions.FirstOrDefault(subscription =>
                subscription.Product?.Handle is not null &&
                string.Equals(subscription.Product.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                !IsEndOfLife(subscription.State));
            if (existingRemote is not null)
            {
                await SaveRecordAsync(user.Id, customer.Id, existingRemote, plan.Handle, cancellationToken);
                return ToDto(existingRemote, plan.Handle);
            }

            if (remoteSubscriptions.Any(subscription => !IsEndOfLife(subscription.State)))
            {
                throw new DuplicateException("This account already has a subscription. Plan changes are not supported by this endpoint.");
            }

            var subscriptionReference = GetSubscriptionReference(customerReference, plan.Handle);
            MaxioSubscription subscription;
            try
            {
                subscription = await _maxio.CreateSubscriptionAsync(
                    customer.Id,
                    plan.Handle,
                    subscriptionReference,
                    cancellationToken);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
            {
                // Billing API's uniqueness_token means the original POST was
                // already accepted. Resolve its result from the system of record.
                var recovered = (await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                    .FirstOrDefault(candidate =>
                        candidate.Product?.Handle is not null &&
                        string.Equals(candidate.Product.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                        !IsEndOfLife(candidate.State));
                if (recovered is null)
                {
                    throw;
                }

                await SaveRecordAsync(user.Id, customer.Id, recovered, plan.Handle, cancellationToken);
                return ToDto(recovered, plan.Handle);
            }

            await SaveRecordAsync(user.Id, customer.Id, subscription, plan.Handle, cancellationToken);
            return ToDto(subscription, plan.Handle);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        var customerReference = GetCustomerReference(user);
        var customer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var result = new List<SubscriptionDto>(subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            var planHandle = subscription.Product?.Handle ??
                await _identityDb.MaxioSubscriptionRecords
                    .Where(record => record.UserId == user.Id && record.MaxioSubscriptionId == subscription.Id)
                    .Select(record => record.ProductHandle)
                    .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;
            result.Add(ToDto(subscription, planHandle));
        }

        return result;
    }

    private async Task<ApplicationUser> GetUserAsync(ClaimsPrincipal principal)
    {
        var userName = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new UnauthorizedAccessException("The bearer token does not identify a user.");
        }

        return await _userManager.FindByNameAsync(userName)
            ?? throw new UnauthorizedAccessException("The authenticated user no longer exists.");
    }

    private async Task SaveRecordAsync(string userId, int customerId, MaxioSubscription subscription, string planHandle, CancellationToken cancellationToken)
    {
        var record = await _identityDb.MaxioSubscriptionRecords
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (record is null)
        {
            _identityDb.MaxioSubscriptionRecords.Add(new MaxioSubscriptionRecord
            {
                UserId = userId,
                MaxioCustomerId = customerId,
                MaxioSubscriptionId = subscription.Id,
                ProductHandle = planHandle,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            record.MaxioCustomerId = customerId;
            record.MaxioSubscriptionId = subscription.Id;
            record.ProductHandle = planHandle;
        }

        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionDto ToDto(MaxioSubscription subscription, string fallbackPlanHandle)
    {
        var product = subscription.Product;
        return new SubscriptionDto
        {
            Id = subscription.Id,
            PlanHandle = product?.Handle ?? fallbackPlanHandle,
            PlanName = product?.Name ?? product?.Handle ?? fallbackPlanHandle,
            PriceInCents = subscription.ProductPriceInCents != 0
                ? subscription.ProductPriceInCents
                : product?.PriceInCents ?? 0,
            Interval = product?.Interval ?? 0,
            IntervalUnit = product?.IntervalUnit ?? string.Empty,
            State = subscription.State ?? string.Empty,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
        };
    }

    private static bool IsEndOfLife(string? state) => state is "canceled" or "expired" or "failed_to_create" or "on_hold" or "suspended" or "trial_ended";
    private static string GetCustomerReference(ApplicationUser user)
    {
        var stableIdentity = (user.UserName ?? user.Email ?? user.Id).Trim().ToLowerInvariant();
        return $"eshop-user:{stableIdentity}";
    }

    private static string GetSubscriptionReference(string customerReference, string planHandle) =>
        $"eshop-subscription:{customerReference}:{planHandle}";
}
