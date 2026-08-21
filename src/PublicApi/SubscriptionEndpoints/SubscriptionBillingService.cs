using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Data.Billing;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionBillingService
{
    private readonly MaxioBillingGateway _maxio;
    private readonly SubscriptionEnrollmentStore _enrollments;
    private readonly SubscriptionOperationLock _operationLock;
    private readonly MaxioOptions _settings;

    public SubscriptionBillingService(
        MaxioBillingGateway maxio,
        SubscriptionEnrollmentStore enrollments,
        SubscriptionOperationLock operationLock,
        IOptions<MaxioOptions> settings)
    {
        _maxio = maxio;
        _enrollments = enrollments;
        _operationLock = operationLock;
        _settings = settings.Value;
    }

    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(
        CancellationToken cancellationToken) =>
        _maxio.ListPlansAsync(cancellationToken);

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(
        CurrentBillingUser user,
        CancellationToken cancellationToken)
    {
        var customer = await _maxio.ReadCustomerAsync(user.CustomerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer, cancellationToken);
        return subscriptions
            .Where(x => string.Equals(
                x.ProductFamilyHandle,
                _settings.ProductFamilyHandle,
                StringComparison.Ordinal))
            .Select(x => x.ToDto())
            .ToList();
    }

    public async Task<SubscriptionCreationResult> SubscribeAsync(
        CurrentBillingUser user,
        string requestedProductHandle,
        CancellationToken cancellationToken)
    {
        var product = await _maxio.ReadEligibleProductAsync(requestedProductHandle.Trim(), cancellationToken);
        var normalizedHandle = product.Handle.ToLowerInvariant();
        var operationKey = $"{user.UserKey}\n{normalizedHandle}";
        using var operationLock = await _operationLock.AcquireAsync(operationKey, cancellationToken);

        var enrollment = await _enrollments.FindAsync(user.UserKey, normalizedHandle, cancellationToken);
        if (enrollment is not null)
        {
            var existing = await ReconcileAsync(user, enrollment, cancellationToken);
            if (existing is not null)
            {
                return new SubscriptionCreationResult(existing.ToDto(), false);
            }

            if (enrollment.Status is SubscriptionEnrollmentStatus.Pending or
                SubscriptionEnrollmentStatus.NeedsReconciliation)
            {
                throw SubscriptionBillingException.InProgress();
            }

            enrollment.Retry();
            await _enrollments.SaveAsync(cancellationToken);
        }
        else
        {
            enrollment = new SubscriptionEnrollment(
                user.UserKey,
                normalizedHandle,
                CurrentBillingUserFactory.SubscriptionReference(user.CustomerReference, product.Handle));

            if (!await _enrollments.TryAddAsync(enrollment, cancellationToken))
            {
                throw SubscriptionBillingException.InProgress();
            }
        }

        MaxioCustomer customer;
        try
        {
            customer = await _maxio.EnsureCustomerAsync(user, cancellationToken);
            var remoteExisting = (await _maxio.ListCustomerSubscriptionsAsync(customer, cancellationToken))
                .SingleOrDefault(x => string.Equals(
                    x.Reference,
                    enrollment.SubscriptionReference,
                    StringComparison.Ordinal));
            if (remoteExisting is not null)
            {
                enrollment.Complete(remoteExisting.Id);
                await _enrollments.SaveAsync(cancellationToken);
                return new SubscriptionCreationResult(remoteExisting.ToDto(), false);
            }

            var created = await _maxio.CreateSubscriptionAsync(
                customer,
                product,
                enrollment.SubscriptionReference,
                cancellationToken);
            enrollment.Complete(created.Id);
            await _enrollments.SaveAsync(cancellationToken);
            return new SubscriptionCreationResult(created.ToDto(), true);
        }
        catch (MaxioWriteOutcomeUnknownException)
        {
            enrollment.MarkNeedsReconciliation();
            await _enrollments.SaveAsync(CancellationToken.None);

            var reconciled = await ReconcileAsync(user, enrollment, cancellationToken);
            if (reconciled is not null)
            {
                return new SubscriptionCreationResult(reconciled.ToDto(), true);
            }

            throw SubscriptionBillingException.InProgress();
        }
        catch (SubscriptionBillingException ex)
        {
            enrollment.Fail(ex.StatusCode == 422 ? "provider_rejected" : "dependency_failure");
            await _enrollments.SaveAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<MaxioSubscription?> ReconcileAsync(
        CurrentBillingUser user,
        SubscriptionEnrollment enrollment,
        CancellationToken cancellationToken)
    {
        var customer = await _maxio.ReadCustomerAsync(user.CustomerReference, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var subscription = (await _maxio.ListCustomerSubscriptionsAsync(customer, cancellationToken))
            .SingleOrDefault(x => string.Equals(
                x.Reference,
                enrollment.SubscriptionReference,
                StringComparison.Ordinal));
        if (subscription is null)
        {
            return null;
        }

        enrollment.Complete(subscription.Id);
        await _enrollments.SaveAsync(cancellationToken);
        return subscription;
    }
}
