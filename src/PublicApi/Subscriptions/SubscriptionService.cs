using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken);
}

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new();
    private readonly IMaxioClient _maxioClient;
    private readonly AppIdentityDbContext _identityDb;
    private readonly MaxioOptions _options;

    public SubscriptionService(IMaxioClient maxioClient, AppIdentityDbContext identityDb, IOptions<MaxioOptions> options)
    {
        _maxioClient = maxioClient;
        _identityDb = identityDb;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxioClient.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products.Where(product => !string.IsNullOrWhiteSpace(product.Handle))
            .Select(ToPlan)
            .ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
            throw new ArgumentException("A product handle is required.", nameof(productHandle));

        var lockKey = $"{user.Id}:{productHandle.Trim().ToLowerInvariant()}";
        var subscriptionLock = SubscriptionLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await subscriptionLock.WaitAsync(cancellationToken);
        try
        {
            var plans = await ListPlansAsync(cancellationToken);
            var plan = plans.FirstOrDefault(item => string.Equals(item.Handle, productHandle.Trim(), StringComparison.OrdinalIgnoreCase));
            if (plan is null)
                throw new SubscriptionPlanNotFoundException(productHandle);

            var subscriptionReference = BuildSubscriptionReference(user.Id, plan.Handle);
            var existing = await _maxioClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                await SaveSubscriptionRecordAsync(user.Id, plan.Handle, existing, cancellationToken);
                return ToSubscription(existing, plan);
            }

            var customer = await GetOrCreateCustomerAsync(user, cancellationToken);
            var subscription = await _maxioClient.CreateSubscriptionAsync(new MaxioCreateSubscription
            {
                ProductHandle = plan.Handle,
                CustomerReference = customer.CustomerReference,
                Reference = subscriptionReference,
                PaymentCollectionMethod = "remittance"
            }, cancellationToken);

            await SaveSubscriptionRecordAsync(user.Id, plan.Handle, subscription, cancellationToken);
            return ToSubscription(subscription, plan);
        }
        finally
        {
            subscriptionLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customerReference = BuildCustomerReference(user.Id);
        var customer = await _maxioClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
            return Array.Empty<SubscriptionDto>();

        var products = await _maxioClient.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        var plans = products.Where(product => !string.IsNullOrWhiteSpace(product.Handle))
            .Select(ToPlan)
            .ToDictionary(plan => plan.Handle, StringComparer.OrdinalIgnoreCase);
        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var result = new List<SubscriptionDto>();

        foreach (var subscription in subscriptions)
        {
            var handle = subscription.Product?.Handle;
            if (string.IsNullOrWhiteSpace(handle) || !plans.TryGetValue(handle, out var plan))
                continue;

            await SaveSubscriptionRecordAsync(user.Id, plan.Handle, subscription, cancellationToken);
            result.Add(ToSubscription(subscription, plan));
        }

        return result.OrderBy(item => item.NextBillingDate).ToArray();
    }

    private async Task<MaxioCustomerRecord> GetOrCreateCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(user.Id);
        var customer = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            var (firstName, lastName) = GetCustomerName(user);
            customer = await _maxioClient.CreateCustomerAsync(new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = user.Email ?? user.UserName ?? $"{user.Id}@invalid.local",
                Reference = reference
            }, cancellationToken);
        }

        var record = await _identityDb.MaxioCustomerRecords.SingleOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);
        if (record is null)
        {
            record = new MaxioCustomerRecord { UserId = user.Id };
            _identityDb.MaxioCustomerRecords.Add(record);
        }

        record.MaxioCustomerId = customer.Id;
        record.CustomerReference = reference;
        await _identityDb.SaveChangesAsync(cancellationToken);
        return record;
    }

    private async Task SaveSubscriptionRecordAsync(string userId, string productHandle, MaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        if (subscription.Id <= 0)
            throw new MaxioApiException(System.Net.HttpStatusCode.BadGateway, "Maxio returned an invalid subscription.");

        var record = await _identityDb.MaxioSubscriptionRecords.SingleOrDefaultAsync(
            item => item.SubscriptionReference == subscription.Reference ||
                    item.UserId == userId && item.ProductHandle == productHandle, cancellationToken);
        if (record is null)
        {
            record = new MaxioSubscriptionRecord
            {
                UserId = userId,
                ProductHandle = productHandle,
                SubscriptionReference = subscription.Reference ?? BuildSubscriptionReference(userId, productHandle),
                CreatedAt = DateTimeOffset.UtcNow
            };
            _identityDb.MaxioSubscriptionRecords.Add(record);
        }

        record.MaxioSubscriptionId = subscription.Id;
        record.SubscriptionReference = subscription.Reference ?? record.SubscriptionReference;
        record.ProductHandle = productHandle;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionPlanDto ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description ?? string.Empty,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static SubscriptionDto ToSubscription(MaxioSubscription subscription, SubscriptionPlanDto fallbackPlan) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? fallbackPlan.Handle,
        PlanName = subscription.Product?.Name ?? fallbackPlan.Name,
        PriceInCents = subscription.CurrentBillingAmountInCents ?? subscription.ProductPriceInCents ?? fallbackPlan.PriceInCents,
        State = subscription.State ?? string.Empty,
        NextBillingDate = subscription.NextAssessmentAt
    };

    private static (string FirstName, string LastName) GetCustomerName(ApplicationUser user)
    {
        var source = (user.Email ?? user.UserName ?? "eShop Shopper").Split('@')[0];
        var pieces = source.Split(new[] { '.', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return pieces.Length > 1
            ? (pieces[0], string.Join(' ', pieces.Skip(1)))
            : (source, "Shopper");
    }

    private static string BuildCustomerReference(string userId) => $"eshop-user-{userId}";

    private static string BuildSubscriptionReference(string userId, string productHandle)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes($"{userId}:{productHandle}"));
        return $"eshop-subscription-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"The subscription plan '{productHandle}' is not available.") { }
}
