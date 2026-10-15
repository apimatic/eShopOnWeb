using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public enum MaxioFailureKind
{
    /// <summary>Maxio refused the request (validation etc.); nothing was created.</summary>
    Rejected,
    /// <summary>Maxio could not be reached / is misconfigured; for a write the provider is known not to have acted... or the read failed.</summary>
    Unavailable,
    /// <summary>A write was sent but its result is unknown (connection failure, timeout, 5xx, unreadable 2xx).</summary>
    UnknownOutcome
}

public class MaxioGatewayException : Exception
{
    public MaxioFailureKind Kind { get; }
    public int? HttpStatus { get; }

    public MaxioGatewayException(MaxioFailureKind kind, string message, int? httpStatus = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        HttpStatus = httpStatus;
    }
}

public record MaxioPlanListing(IReadOnlyList<SubscriptionPlan> Plans, bool IsComplete);

/// <summary>The only place that talks to Maxio. Every failure surfaces as <see cref="MaxioGatewayException"/>.</summary>
public interface IMaxioGateway
{
    Task<MaxioPlanListing> ListPlansAsync(CancellationToken cancellationToken);

    /// <returns>null when no customer has that reference.</returns>
    Task<int?> FindCustomerIdByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<int> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken);

    /// <returns>null when no subscription has that reference.</returns>
    Task<SubscriptionSummary?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<SubscriptionSummary> CreateSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionSummary>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}
