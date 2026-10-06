using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class SubscribeRequest
{
    public string UserId { get; }
    public string Email { get; }
    /// <summary>
    /// Handle of the plan to subscribe to. When null, the service picks the
    /// first plan published by the configured product family.
    /// </summary>
    public string? ProductHandle { get; }

    public SubscribeRequest(string userId, string email, string? productHandle)
    {
        UserId = userId;
        Email = email;
        ProductHandle = productHandle;
    }
}

/// <summary>
/// Subscription enrollment backed by the external billing system of record.
/// All operations are idempotent per user.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionSummary> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default);
}