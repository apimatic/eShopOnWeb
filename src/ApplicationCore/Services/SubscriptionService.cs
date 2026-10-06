using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Subscription use cases on top of the billing gateway. All business rules of the
/// subscribe flow live here; transport concerns stay in Infrastructure.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    /// <summary>
    /// Plans are billed without a card in this integration, so enrollment uses invoice based
    /// collection ("remittance"): Maxio issues the invoice, no payment profile is required at signup.
    /// </summary>
    private const string EnrollmentPaymentCollectionMethod = PaymentCollectionMethods.Remittance;

    private readonly IBillingGateway _gateway;
    private readonly IKeyedLock _keyedLock;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(
        IBillingGateway gateway,
        IKeyedLock keyedLock,
        IAppLogger<SubscriptionService> logger)
    {
        _gateway = gateway;
        _keyedLock = keyedLock;
        _logger = logger;
    }

    public Task<BillingCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
        => _gateway.GetCatalogAsync(cancellationToken);

    public async Task<SubscribeOutcome> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (string.IsNullOrWhiteSpace(command.PlanHandle))
            throw new ArgumentException("A plan handle is required to subscribe.", nameof(command));

        var profile = command.Profile;
        if (profile is null ||
            string.IsNullOrWhiteSpace(profile.Reference) ||
            string.IsNullOrWhiteSpace(profile.Email) ||
            string.IsNullOrWhiteSpace(profile.FirstName) ||
            string.IsNullOrWhiteSpace(profile.LastName))
        {
            throw new ArgumentException(
                "A billing profile with reference, first name, last name and email is required to subscribe.",
                nameof(command));
        }

        // Serialize per (account, plan): a double-click, or two requests for the same plan in
        // flight, can only ever produce one customer and one enrollment.
        var lockKey = $"{nameof(SubscribeAsync)}:{profile.Reference.ToLowerInvariant()}:{command.PlanHandle.ToLowerInvariant()}";

        return await _keyedLock.RunAsync(lockKey, async () =>
        {
            var catalog = await _gateway.GetCatalogAsync(cancellationToken);
            var plan = catalog.Plans.FirstOrDefault(p =>
                string.Equals(p.Handle, command.PlanHandle, StringComparison.OrdinalIgnoreCase));

            if (plan is null || plan.Archived)
            {
                _logger.LogWarning(
                    "Subscribe rejected: plan '{PlanHandle}' is not part of the configured subscription catalog.",
                    command.PlanHandle);
                throw new SubscriptionPlanNotFoundException(command.PlanHandle);
            }

            var customer = await _gateway.EnsureCustomerAsync(profile, cancellationToken);

            var existing = await _gateway.ListSubscriptionsAsync(customer.Id, cancellationToken);
            var liveEnrollment = existing.FirstOrDefault(s =>
                string.Equals(s.PlanHandle, plan.Handle, StringComparison.OrdinalIgnoreCase) && s.State.IsActive());

            if (liveEnrollment is not null)
            {
                _logger.LogInformation(
                    "Subscribe replay for account '{Reference}' on plan '{PlanHandle}': returning existing subscription {SubscriptionId} (state {State}).",
                    customer.Reference, plan.Handle, liveEnrollment.Id, liveEnrollment.State);

                return new SubscribeOutcome(liveEnrollment, customer, Created: false);
            }

            var enrollment = new SubscriptionEnrollment(
                customer.Reference,
                plan.Handle,
                SubscriptionReferenceFor(customer.Reference, plan.Handle),
                EnrollmentPaymentCollectionMethod);

            _logger.LogInformation(
                "Enrolling account '{Reference}' (Maxio customer {CustomerId}) on plan '{PlanHandle}'.",
                customer.Reference, customer.Id, plan.Handle);

            var created = await _gateway.EnrollAsync(enrollment, cancellationToken);

            // Re-read the enrollment so the confirmation we hand back to the shopper reflects
            // the state Maxio settled on (signup can move a subscription out of "provisioning").
            var confirmed = await _gateway.GetSubscriptionAsync(created.Id, cancellationToken) ?? created;

            _logger.LogInformation(
                "Subscribed account '{Reference}' to '{PlanHandle}': subscription {SubscriptionId}, state {State}, next billing {NextBilling:u}.",
                customer.Reference, plan.Handle, confirmed.Id, confirmed.State, confirmed.NextBillingAt);

            return new SubscribeOutcome(confirmed, customer, Created: true);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<UserSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
            throw new ArgumentException("A customer reference is required.", nameof(customerReference));

        var customer = await _gateway.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
            return Array.Empty<UserSubscription>();

        return await _gateway.ListSubscriptionsAsync(customer.Id, cancellationToken);
    }

    /// <summary>
    /// Deterministic enrollment reference. Two subscribes for the same account/plan pair always
    /// produce the same value, which makes the duplicate check independent of local storage.
    /// </summary>
    private static string SubscriptionReferenceFor(string customerReference, string planHandle)
        => $"eshop:{customerReference}:{planHandle}".ToLowerInvariant();
}
