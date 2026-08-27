using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class SubscriptionBillingService(
    IMaxioBillingGateway maxio,
    CatalogContext dbContext,
    UserManager<ApplicationUser> userManager,
    SubscriptionOperationLock operationLock) : ISubscriptionBillingService
{
    public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken) =>
        maxio.GetPlansAsync(cancellationToken);

    public async Task<EnrollmentResult> SubscribeAsync(ClaimsPrincipal principal, string productHandle,
        CancellationToken cancellationToken)
    {
        var identity = await ResolveIdentityAsync(principal);
        var plans = await maxio.GetPlansAsync(cancellationToken);
        if (!plans.Any(plan => string.Equals(plan.Handle, productHandle, StringComparison.Ordinal)))
        {
            throw new SubscriptionPlanNotFoundException();
        }

        var lockKey = string.Concat(identity.UserId, "\n", productHandle);
        using var heldLock = await operationLock.AcquireAsync(lockKey, cancellationToken);

        var enrollment = await dbContext.SubscriptionEnrollments
            .SingleOrDefaultAsync(item => item.UserId == identity.UserId &&
                item.ProductHandle == productHandle, cancellationToken);
        var isClaimOwner = enrollment is null;
        if (enrollment is null)
        {
            enrollment = new SubscriptionEnrollment(identity.UserId, productHandle,
                $"eshop-sub-{Guid.NewGuid():N}");
            dbContext.SubscriptionEnrollments.Add(enrollment);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                dbContext.Entry(enrollment).State = EntityState.Detached;
                enrollment = await dbContext.SubscriptionEnrollments.SingleAsync(item =>
                    item.UserId == identity.UserId && item.ProductHandle == productHandle,
                    cancellationToken);
                isClaimOwner = false;
            }
        }

        var reconciled = await maxio.FindSubscriptionAsync(enrollment.SubscriptionReference,
            cancellationToken);
        if (reconciled is not null)
        {
            var customerId = enrollment.MaxioCustomerId;
            if (customerId is null)
            {
                var reconciledCustomer = await maxio.FindCustomerAsync(identity.CustomerReference,
                    cancellationToken);
                if (reconciledCustomer is null)
                {
                    throw new MaxioProviderException(
                        "The subscription customer could not be reconciled with Maxio.", 502);
                }

                customerId = reconciledCustomer.Id;
            }

            enrollment.Complete(customerId.Value, reconciled.SubscriptionId);
            await dbContext.SaveChangesAsync(cancellationToken);
            return EnrollmentResult.Completed(reconciled);
        }

        if (!isClaimOwner)
        {
            if (enrollment.Status == SubscriptionEnrollmentStatus.Failed)
            {
                throw new MaxioProviderException("The subscription enrollment previously failed.", 409);
            }

            return EnrollmentResult.Pending();
        }

        MaxioCustomer customer;
        try
        {
            customer = await EnsureCustomerAsync(identity, cancellationToken);
        }
        catch (MaxioProviderException ex) when (ex.OutcomeUnknown)
        {
            enrollment.MarkReconciling(null);
            await dbContext.SaveChangesAsync(cancellationToken);
            return EnrollmentResult.Pending();
        }
        catch
        {
            enrollment.MarkFailed(null);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        try
        {
            var subscription = await maxio.CreateSubscriptionAsync(productHandle, customer.Id,
                enrollment.SubscriptionReference, cancellationToken);
            enrollment.Complete(customer.Id, subscription.SubscriptionId);
            await dbContext.SaveChangesAsync(cancellationToken);
            return EnrollmentResult.Completed(subscription);
        }
        catch (MaxioProviderException ex) when (ex.OutcomeUnknown)
        {
            var afterAmbiguousWrite = await TryReconcileAsync(enrollment.SubscriptionReference,
                cancellationToken);
            if (afterAmbiguousWrite is not null)
            {
                enrollment.Complete(customer.Id, afterAmbiguousWrite.SubscriptionId);
                await dbContext.SaveChangesAsync(cancellationToken);
                return EnrollmentResult.Completed(afterAmbiguousWrite);
            }

            enrollment.MarkReconciling(customer.Id);
            await dbContext.SaveChangesAsync(cancellationToken);
            return EnrollmentResult.Pending();
        }
        catch
        {
            enrollment.MarkFailed(customer.Id);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(
        ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var identity = await ResolveIdentityAsync(principal);
        var customer = await maxio.FindCustomerAsync(identity.CustomerReference, cancellationToken);
        return customer is null
            ? Array.Empty<SubscriptionDto>()
            : await maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        BillingCustomerIdentity identity, CancellationToken cancellationToken)
    {
        var existing = await maxio.FindCustomerAsync(identity.CustomerReference, cancellationToken);
        if (existing is not null) return existing;

        try
        {
            return await maxio.CreateCustomerAsync(identity, cancellationToken);
        }
        catch (MaxioProviderException ex) when (ex.ProviderStatusCode == 422)
        {
            var racedCustomer = await maxio.FindCustomerAsync(identity.CustomerReference, cancellationToken);
            if (racedCustomer is not null) return racedCustomer;
            throw;
        }
    }

    private async Task<SubscriptionDto?> TryReconcileAsync(string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            return await maxio.FindSubscriptionAsync(reference, cancellationToken);
        }
        catch (MaxioProviderException)
        {
            return null;
        }
    }

    private async Task<BillingCustomerIdentity> ResolveIdentityAsync(ClaimsPrincipal principal)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName)) throw new BillingIdentityException();

        var user = await userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrWhiteSpace(user.Id) || string.IsNullOrWhiteSpace(user.Email))
        {
            throw new BillingIdentityException();
        }

        var localPart = user.Email.Split('@', 2)[0];
        var token = localPart.Split(['.', '_', '-', '+'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "eShop";
        var firstName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(token.ToLowerInvariant());

        return new BillingCustomerIdentity(user.Id, user.Email, firstName, "Customer",
            $"eshop-user-{user.Id}");
    }
}
