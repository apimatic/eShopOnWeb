using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The port onto the external billing system of record. Implementations are responsible for
/// transport concerns only - retries, deserialization, error translation - and never for the
/// enrollment rules, which live in <see cref="ISubscriptionService"/>.
/// </summary>
public interface IBillingGateway
{
    /// <summary>Lists the plans currently offered, i.e. the non-archived products of the configured family.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the plan with the given handle, or null when no such plan is offered.</summary>
    Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Returns the customer carrying the given reference, or null when there is none.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer. Throws <see cref="Exceptions.DuplicateBillingReferenceException"/> when the
    /// reference is already taken, which is how a lost-then-retried create surfaces.
    /// </summary>
    Task<BillingCustomer> CreateCustomerAsync(NewCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to a customer, in any state.</summary>
    Task<IReadOnlyCollection<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a customer in a plan. Throws <see cref="Exceptions.DuplicateBillingSubmissionException"/>
    /// when the request repeats a uniqueness token the billing system has already seen.
    /// </summary>
    Task<CustomerSubscription> CreateSubscriptionAsync(NewSubscriptionRequest request, CancellationToken cancellationToken = default);
}
