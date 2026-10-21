using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Our own record of one attempt by one user to subscribe to one plan. It is the duplicate claim:
/// its primary key (<see cref="Id"/> = user, plan, attempt) is inserted before any billing call, so a
/// second click computes the same key and is refused by the store. It also keeps, for each write the
/// signup makes, the duplicate-prevention token sent to the billing system, so every resend of that
/// write carries the same one.
/// </summary>
public class SubscriptionSignup : IAggregateRoot
{
    /// <summary>The longest one worker may hold a signup before another may resume it.</summary>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(6);

    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionSignup() { }
    #pragma warning restore CS8618

    public SubscriptionSignup(string userId, string customerEmail, string planHandle, int attempt, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(customerEmail, nameof(customerEmail));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Guard.Against.NegativeOrZero(attempt, nameof(attempt));

        Id = KeyFor(userId, planHandle, attempt);
        UserId = userId;
        CustomerEmail = customerEmail;
        PlanHandle = planHandle;
        Attempt = attempt;
        CustomerReference = CustomerReferenceFor(userId);
        SubscriptionReference = $"eshop-{userId}-{planHandle}-{attempt}";
        CustomerUniquenessToken = NewToken();
        SubscriptionUniquenessToken = NewToken();
        Status = SubscriptionSignupStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
        LeaseId = Guid.NewGuid();
        LeaseExpiresAt = now + LeaseDuration;
        Version = Guid.NewGuid();
    }

    /// <summary>The claim key: one row per user, plan and attempt.</summary>
    public string Id { get; private set; }
    public string UserId { get; private set; }
    public string CustomerEmail { get; private set; }
    public string PlanHandle { get; private set; }
    public int Attempt { get; private set; }

    /// <summary>The billing customer's reference: the same for every signup of this user.</summary>
    public string CustomerReference { get; private set; }

    /// <summary>The subscription's reference, derived from this record, so every resend carries the same one.</summary>
    public string SubscriptionReference { get; private set; }

    public string CustomerUniquenessToken { get; private set; }
    public DateTimeOffset? CustomerCreateFirstSentAt { get; private set; }
    public bool CustomerCreateConflicted { get; private set; }

    public string SubscriptionUniquenessToken { get; private set; }
    public DateTimeOffset? SubscriptionCreateFirstSentAt { get; private set; }
    public bool SubscriptionCreateConflicted { get; private set; }

    public SubscriptionSignupStatus Status { get; private set; }
    public int? BillingSubscriptionId { get; private set; }
    public string? ActionLink { get; private set; }
    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? LeaseId { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    /// <summary>Optimistic-concurrency token: a lease is taken by an update that only one worker can win.</summary>
    public Guid Version { get; private set; }

    public bool IsOpen => Status is SubscriptionSignupStatus.Pending or SubscriptionSignupStatus.Unknown
        or SubscriptionSignupStatus.ActionRequired;

    public static string KeyFor(string userId, string planHandle, int attempt) => $"{userId}:{planHandle}:{attempt}";

    public static string CustomerReferenceFor(string userId) => $"eshop-{userId}";

    public bool IsLeased(DateTimeOffset now) => LeaseExpiresAt is { } expires && expires > now;

    /// <summary>Takes the lease. Saving the change fails for every worker but one (see <see cref="Version"/>).</summary>
    public Guid AcquireLease(DateTimeOffset now)
    {
        LeaseId = Guid.NewGuid();
        LeaseExpiresAt = now + LeaseDuration;
        Touch(now);
        return LeaseId.Value;
    }

    public void ReleaseLease(DateTimeOffset now)
    {
        LeaseId = null;
        LeaseExpiresAt = null;
        Touch(now);
    }

    public void RecordCustomerCreateSent(DateTimeOffset now)
    {
        CustomerCreateFirstSentAt ??= now;
        Touch(now);
    }

    public void RecordCustomerCreateUnknown(bool conflicted, string reason, DateTimeOffset now)
    {
        CustomerCreateConflicted = conflicted;
        Status = SubscriptionSignupStatus.Unknown;
        LastError = reason;
        Touch(now);
    }

    /// <summary>
    /// A new customer-create token, allowed only after a corrected rejection, or after a conflict once
    /// the read-back window has passed with nothing found.
    /// </summary>
    public void RenewCustomerToken(DateTimeOffset now)
    {
        CustomerUniquenessToken = NewToken();
        CustomerCreateFirstSentAt = null;
        CustomerCreateConflicted = false;
        Touch(now);
    }

    public void RecordSubscriptionCreateSent(DateTimeOffset now)
    {
        SubscriptionCreateFirstSentAt ??= now;
        Touch(now);
    }

    public void RecordSubscriptionCreateUnknown(bool conflicted, string reason, DateTimeOffset now)
    {
        SubscriptionCreateConflicted = conflicted;
        Status = SubscriptionSignupStatus.Unknown;
        LastError = reason;
        Touch(now);
    }

    /// <summary>See <see cref="RenewCustomerToken"/>; the same rule for the subscription create.</summary>
    public void RenewSubscriptionToken(DateTimeOffset now)
    {
        SubscriptionUniquenessToken = NewToken();
        SubscriptionCreateFirstSentAt = null;
        SubscriptionCreateConflicted = false;
        Touch(now);
    }

    public void RecordActionRequired(string actionLink, DateTimeOffset now)
    {
        Status = SubscriptionSignupStatus.ActionRequired;
        ActionLink = actionLink;
        Touch(now);
    }

    public void Complete(int billingSubscriptionId, DateTimeOffset now)
    {
        Status = SubscriptionSignupStatus.Completed;
        BillingSubscriptionId = billingSubscriptionId;
        ActionLink = null;
        LastError = null;
        Touch(now);
    }

    public void Reject(string reason, DateTimeOffset now)
    {
        Status = SubscriptionSignupStatus.Rejected;
        LastError = reason;
        Touch(now);
    }

    /// <summary>The subscription this signup made has ended, so the user may sign up for the plan again.</summary>
    public void MarkEnded(DateTimeOffset now)
    {
        Status = SubscriptionSignupStatus.Ended;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    private static string NewToken() => Guid.NewGuid().ToString("N");
}
