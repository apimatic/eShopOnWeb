using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public sealed partial class SubscriptionService : ISubscriptionService
{
    private const string CustomerReferencePrefix = "eshop-c-v1-";
    private const string SubscriptionReferencePrefix = "eshop-s-v1-";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);
    private readonly IMaxioBillingGateway _gateway;
    private readonly ISubscriptionEnrollmentStore _enrollments;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionService(
        IMaxioBillingGateway gateway,
        ISubscriptionEnrollmentStore enrollments,
        UserManager<ApplicationUser> userManager)
    {
        _gateway = gateway;
        _enrollments = enrollments;
        _userManager = userManager;
    }

    public Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken) =>
        _gateway.ListPlansAsync(cancellationToken);

    public async Task<SubscriptionResult> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken)
    {
        productHandle = NormalizeHandle(productHandle);
        var user = await ResolveUserAsync(userName, cancellationToken);
        var lockKey = $"{user.Id}\n{productHandle}";
        var gate = Locks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));
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

    public async Task<IReadOnlyList<BillingSubscription>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(userName, cancellationToken);
        return await _gateway.ListCustomerSubscriptionsAsync(
            CustomerReference(user.Id),
            SubscriptionReferencePrefix,
            cancellationToken);
    }

    private async Task<SubscriptionResult> SubscribeCoreAsync(ApplicationUser user, string productHandle, CancellationToken cancellationToken)
    {
        var product = await _gateway.FindProductAsync(productHandle, cancellationToken);
        if (product is null)
        {
            throw new BillingException("plan_not_found", "The requested subscription plan was not found.", HttpStatusCode.NotFound);
        }
        if (product.RequiresPaymentMethod)
        {
            throw new BillingException("payment_method_required", "This plan requires a payment method and is not available through this subscription flow.", HttpStatusCode.UnprocessableEntity);
        }

        var customerReference = CustomerReference(user.Id);
        var subscriptionReference = SubscriptionReference(user.Id, productHandle);
        var enrollment = await _enrollments.GetOrCreateAsync(user.Id, productHandle, customerReference, subscriptionReference, cancellationToken);

        var existing = await _gateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            await _enrollments.CompleteAsync(enrollment.Id, null, existing.Id, cancellationToken);
            return new SubscriptionResult(existing, false);
        }

        var leaseOwner = Guid.NewGuid().ToString("N");
        if (!await _enrollments.TryAcquireLeaseAsync(enrollment.Id, leaseOwner, DateTimeOffset.UtcNow, cancellationToken))
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                existing = await _gateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (existing is not null)
                {
                    return new SubscriptionResult(existing, false);
                }
            }
            throw new BillingException("subscription_in_progress", "This subscription request is already being processed.", HttpStatusCode.Conflict);
        }

        try
        {
            var profile = BuildProfile(user, customerReference);
            var customer = await _gateway.EnsureCustomerAsync(profile, cancellationToken);
            var subscription = await _gateway.CreateSubscriptionAsync(productHandle, customerReference, subscriptionReference, cancellationToken);
            await _enrollments.CompleteAsync(enrollment.Id, customer.Id, subscription.Id, cancellationToken);
            return new SubscriptionResult(subscription, true);
        }
        catch (BillingException ex)
        {
            await _enrollments.FailAsync(enrollment.Id, ex.Code, cancellationToken);
            throw;
        }
    }

    private async Task<ApplicationUser> ResolveUserAsync(string userName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingException("identity_missing", "The authenticated user identity is invalid.", HttpStatusCode.Unauthorized);
        }
        var user = await _userManager.FindByNameAsync(userName);
        cancellationToken.ThrowIfCancellationRequested();
        return user ?? throw new BillingException("identity_not_found", "The authenticated user no longer exists.", HttpStatusCode.Unauthorized);
    }

    private static BillingCustomerProfile BuildProfile(ApplicationUser user, string customerReference)
    {
        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new BillingException("profile_incomplete", "An email address is required before subscribing.", HttpStatusCode.UnprocessableEntity);
        }

        var localPart = email.Split('@', 2)[0];
        var words = Regex.Split(localPart, "[^A-Za-z0-9]+", RegexOptions.CultureInvariant)
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        var firstName = words.FirstOrDefault() ?? "eShop";
        var lastName = words.Skip(1).FirstOrDefault() ?? "Customer";
        return new BillingCustomerProfile(firstName, lastName, email, customerReference);
    }

    private static string NormalizeHandle(string productHandle)
    {
        var normalized = productHandle?.Trim() ?? string.Empty;
        if (!ProductHandlePattern().IsMatch(normalized))
        {
            throw new BillingException("invalid_product_handle", "A valid productHandle is required.", HttpStatusCode.BadRequest);
        }
        return normalized;
    }

    private static string CustomerReference(string userId) => CustomerReferencePrefix + Digest(userId);
    private static string SubscriptionReference(string userId, string productHandle) =>
        SubscriptionReferencePrefix + Digest($"{userId}\n{productHandle}");

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..40];

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,254}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProductHandlePattern();
}
