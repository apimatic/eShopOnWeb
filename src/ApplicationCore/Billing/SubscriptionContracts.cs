using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public record SubscriptionPlan(string Handle, string Name, string? Description, long? PriceInCents,
    int? Interval, string? IntervalUnit, bool? RequiresPaymentMethod,
    bool? RequiresBillingAddress, bool? RequiresShippingAddress);
public record BillingAccount(string UserId, string Email, string FirstName, string LastName);
public record BillingCustomer(int Id, string Reference);
public record SubscriptionDetails(int Id, string? ProductHandle, string? PlanName,
    long? PriceInCents, long? CurrentBillingAmountInCents, string? Currency, string? State,
    DateTimeOffset? NextBillingDate, int? Interval, string? IntervalUnit);
public record OwnedSubscription(SubscriptionDetails Details, int CustomerId, string CustomerReference, string? Reference);
public record EnrollmentResult(string Status, SubscriptionDetails? Subscription);
public record PendingEnrollment(string ProductHandle, string Status);
public record MySubscriptions(IReadOnlyList<SubscriptionDetails> Subscriptions, IReadOnlyList<PendingEnrollment> Pending);

public interface ISubscriptionGateway
{
    string Scope { get; }
    string ProductFamilyHandle { get; }
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken ct);
    Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken ct);
    Task<BillingCustomer> CreateCustomerAsync(BillingAccount account, string reference, CancellationToken ct);
    Task<IReadOnlyList<OwnedSubscription>> GetSubscriptionsAsync(int customerId, CancellationToken ct);
    Task<OwnedSubscription?> FindSubscriptionAsync(string reference, CancellationToken ct);
    Task<OwnedSubscription> CreateSubscriptionAsync(int customerId, string handle, string reference, CancellationToken ct);
}

// Only caller-safe messages cross this boundary; SDK response bodies never do.
public class BillingException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }
    public bool OutcomeUnknown { get; }
    public BillingException(string code, string message, int statusCode = 502, bool outcomeUnknown = false)
        : base(message) => (Code, StatusCode, OutcomeUnknown) = (code, statusCode, outcomeUnknown);
}

public class BillingClaim
{
    public string Id { get; set; } = "";
    public string Scope { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public string Reference { get; set; } = "";
    public string State { get; set; } = "Pending";
    public int? ProviderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public interface IBillingClaimStore
{
    Task<(bool Acquired, BillingClaim Claim)> AcquireAsync(BillingClaim claim, CancellationToken ct);
    Task CompleteAsync(string id, int providerId, CancellationToken ct);
    Task MarkUnknownAsync(string id, CancellationToken ct);
    Task ReleaseAsync(string id, CancellationToken ct, string? expectedState = null);
    Task<bool> TryBeginEnrollmentAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<BillingClaim>> GetPendingAsync(string scope, string userId, CancellationToken ct);
    Task<IReadOnlyList<BillingClaim>> GetUnsettledAsync(string scope, CancellationToken ct);
}
