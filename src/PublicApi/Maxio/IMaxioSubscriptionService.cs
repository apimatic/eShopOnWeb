using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The authenticated eShopOnWeb user, projected onto the data Maxio needs.
/// UserId is the stable Identity user id and doubles as the Maxio customer reference.
/// </summary>
public record SubscriptionUserContext(string UserId, string Email, string FirstName, string LastName);

public class SubscriptionPlanDto
{
    public int? Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public decimal? Price { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class SubscriptionDto
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public decimal? Price { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct);

    /// <summary>
    /// Idempotent subscribe: finds or creates the Maxio customer (reference = user id),
    /// returns an existing live subscription for the plan if one exists, otherwise creates it.
    /// </summary>
    Task<SubscriptionDto> SubscribeAsync(SubscriptionUserContext user, string productHandle, CancellationToken ct);

    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userId, CancellationToken ct);
}
