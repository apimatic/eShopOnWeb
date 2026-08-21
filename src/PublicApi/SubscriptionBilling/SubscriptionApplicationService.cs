using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public interface ISubscriptionApplicationService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListMineAsync(CancellationToken cancellationToken);
}

public sealed class SubscriptionApplicationService : ISubscriptionApplicationService
{
    private static readonly TimeSpan ProcessingWait = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan StaleProcessingAge = TimeSpan.FromMinutes(2);

    private readonly IMaxioBillingGateway _gateway;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CatalogContext _catalogContext;
    private readonly SubscriptionIntentCoordinator _coordinator;

    public SubscriptionApplicationService(
        IMaxioBillingGateway gateway,
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager,
        CatalogContext catalogContext,
        SubscriptionIntentCoordinator coordinator)
    {
        _gateway = gateway;
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
        _catalogContext = catalogContext;
        _coordinator = coordinator;
    }

    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken) =>
        _gateway.ListPlansAsync(cancellationToken);

    public async Task<SubscriptionDto> SubscribeAsync(
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle) || productHandle.Length > 255)
        {
            throw new ArgumentException("A valid productHandle is required.", nameof(productHandle));
        }

        var user = await GetCurrentUserAsync();
        var userId = user.UserName!;
        await _gateway.GetPlanAsync(productHandle, cancellationToken);

        var customerReference = CreateReference("eshop-c", userId);
        var subscriptionReference = CreateReference("eshop-s", $"{userId}\n{productHandle}");
        var lockKey = $"{userId}\n{productHandle}";
        await using var intentLock = await _coordinator.AcquireAsync(lockKey, cancellationToken);

        var intent = await FindIntentAsync(userId, productHandle, cancellationToken);
        var ownsNewIntent = false;
        if (intent is null)
        {
            intent = new SubscriptionIntent(userId, productHandle, subscriptionReference);
            _catalogContext.SubscriptionIntents.Add(intent);
            try
            {
                await _catalogContext.SaveChangesAsync(cancellationToken);
                ownsNewIntent = true;
            }
            catch (DbUpdateException)
            {
                _catalogContext.ChangeTracker.Clear();
                intent = await FindIntentAsync(userId, productHandle, cancellationToken);
                if (intent is null)
                {
                    throw;
                }
            }
        }

        if (!ownsNewIntent && intent.Status == SubscriptionIntentStatus.Processing)
        {
            intent = await WaitForProcessingOwnerAsync(intent, cancellationToken);
        }

        if (intent.Status == SubscriptionIntentStatus.Active)
        {
            return await RequireReconciledSubscriptionAsync(intent, cancellationToken);
        }

        if (intent.Status == SubscriptionIntentStatus.OutcomeUnknown)
        {
            var recovered = await _gateway.FindSubscriptionAsync(intent.SubscriptionReference, cancellationToken);
            if (recovered is null)
            {
                throw new BillingProviderException(
                    BillingProviderFailureKind.OutcomeUnknown,
                    "The earlier subscription outcome is still being reconciled.");
            }

            intent.MarkActive(intent.MaxioCustomerId, recovered.Id);
            await _catalogContext.SaveChangesAsync(cancellationToken);
            return recovered;
        }

        if (intent.Status == SubscriptionIntentStatus.Rejected)
        {
            intent.MarkProcessing();
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }

        var preexisting = await _gateway.FindSubscriptionAsync(intent.SubscriptionReference, cancellationToken);
        if (preexisting is not null)
        {
            intent.MarkActive(intent.MaxioCustomerId, preexisting.Id);
            await _catalogContext.SaveChangesAsync(cancellationToken);
            return preexisting;
        }

        var profile = CreateCustomerProfile(user);
        BillingCustomer? customer = null;
        var createAttempted = false;
        try
        {
            customer = await _gateway.EnsureCustomerAsync(customerReference, profile, cancellationToken);
            createAttempted = true;
            var subscription = await _gateway.CreateSubscriptionAsync(
                productHandle,
                customerReference,
                intent.SubscriptionReference,
                cancellationToken);
            intent.MarkActive(customer.Id, subscription.Id);
            await _catalogContext.SaveChangesAsync(cancellationToken);
            return subscription;
        }
        catch (BillingProviderException failure)
        {
            if (createAttempted)
            {
                var recovered = await TryReconcileAfterWriteAsync(intent.SubscriptionReference, cancellationToken);
                if (recovered is not null)
                {
                    intent.MarkActive(customer?.Id, recovered.Id);
                    await _catalogContext.SaveChangesAsync(cancellationToken);
                    return recovered;
                }
            }

            if (createAttempted && failure.Kind != BillingProviderFailureKind.Rejected)
            {
                intent.MarkOutcomeUnknown();
            }
            else
            {
                intent.MarkRejected();
            }

            await _catalogContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMineAsync(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        return await _gateway.ListSubscriptionsAsync(
            CreateReference("eshop-c", user.UserName!),
            cancellationToken);
    }

    private async Task<ApplicationUser> GetCurrentUserAsync()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var userName = principal?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new UnauthorizedAccessException();
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrWhiteSpace(user.Email) || string.IsNullOrWhiteSpace(user.UserName))
        {
            throw new UnauthorizedAccessException();
        }

        return user;
    }

    private async Task<SubscriptionIntent?> FindIntentAsync(
        string userId,
        string productHandle,
        CancellationToken cancellationToken) =>
        await _catalogContext.SubscriptionIntents.SingleOrDefaultAsync(
            intent => intent.UserId == userId && intent.ProductHandle == productHandle,
            cancellationToken);

    private async Task<SubscriptionIntent> WaitForProcessingOwnerAsync(
        SubscriptionIntent intent,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + ProcessingWait;
        while (intent.Status == SubscriptionIntentStatus.Processing && DateTimeOffset.UtcNow < deadline)
        {
            if (DateTimeOffset.UtcNow - intent.UpdatedAt >= StaleProcessingAge)
            {
                var recovered = await _gateway.FindSubscriptionAsync(intent.SubscriptionReference, cancellationToken);
                if (recovered is not null)
                {
                    intent.MarkActive(intent.MaxioCustomerId, recovered.Id);
                }
                else
                {
                    intent.MarkOutcomeUnknown();
                }

                await _catalogContext.SaveChangesAsync(cancellationToken);
                return intent;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            _catalogContext.ChangeTracker.Clear();
            intent = await _catalogContext.SubscriptionIntents.SingleAsync(
                item => item.Id == intent.Id,
                cancellationToken);
        }

        if (intent.Status == SubscriptionIntentStatus.Processing)
        {
            throw new SubscriptionEnrollmentInProgressException();
        }

        return intent;
    }

    private async Task<SubscriptionDto> RequireReconciledSubscriptionAsync(
        SubscriptionIntent intent,
        CancellationToken cancellationToken)
    {
        var subscription = await _gateway.FindSubscriptionAsync(intent.SubscriptionReference, cancellationToken);
        if (subscription is null)
        {
            intent.MarkOutcomeUnknown();
            await _catalogContext.SaveChangesAsync(cancellationToken);
            throw new BillingProviderException(
                BillingProviderFailureKind.Protocol,
                "The subscription record could not be reconciled with Maxio.");
        }

        return subscription;
    }

    private async Task<SubscriptionDto?> TryReconcileAfterWriteAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _gateway.FindSubscriptionAsync(reference, cancellationToken);
        }
        catch (BillingProviderException)
        {
            return null;
        }
    }

    private static BillingCustomerProfile CreateCustomerProfile(ApplicationUser user)
    {
        var email = user.Email!;
        var localPart = email.Split('@', 2)[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
        return new BillingCustomerProfile(firstName, "Customer", email);
    }

    private static string CreateReference(string prefix, string source)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"{prefix}-{Convert.ToHexString(digest.AsSpan(0, 20)).ToLowerInvariant()}";
    }
}
