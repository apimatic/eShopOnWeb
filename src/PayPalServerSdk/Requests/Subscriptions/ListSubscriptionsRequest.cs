using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using PayPalServerSdk.Core.Validation.Attributes;

namespace PayPalServerSdk.Requests.Subscriptions;

/// <summary>
/// The inputs of the ListSubscriptions operation.
/// </summary>
public sealed record ListSubscriptionsRequest
{
    /// <summary>
    /// Filters the response by list of plan IDs. Filter supports upto 70 plan IDs. URLs should not exceed a length of 2000 characters.
    /// </summary>
    public string? PlanIds { get; init; }

    /// <summary>
    /// Filters the response by list of subscription statuses.
    /// </summary>
    [StringLength(70, MinimumLength = 1)]
    [RegularExpression("^[A-Z_,]+$")]
    public string? Statuses { get; init; }

    /// <summary>
    /// Filters the response by subscription creation start time for a range of subscriptions.
    /// </summary>
    [StringLength(64, MinimumLength = 20)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])[T,t]([0-1][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)([.][0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    public string? CreatedAfter { get; init; }

    /// <summary>
    /// Filters the response by subscription creation end time for a range of subscriptions.
    /// </summary>
    [StringLength(64, MinimumLength = 20)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])[T,t]([0-1][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)([.][0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    public string? CreatedBefore { get; init; }

    /// <summary>
    /// Filters the response by status update start time for a range of subscriptions.
    /// </summary>
    [StringLength(64, MinimumLength = 20)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])[T,t]([0-1][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)([.][0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    public string? StatusUpdatedBefore { get; init; }

    /// <summary>
    /// Filters the response by status update end time for a range of subscriptions.
    /// </summary>
    [StringLength(64, MinimumLength = 20)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])[T,t]([0-1][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)([.][0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    public string? StatusUpdatedAfter { get; init; }

    /// <summary>
    /// Filter the response using complex expressions that could use comparison operators like ge, gt, le, lt and logical operators such as 'and' and 'or'.
    /// </summary>
    [StringLength(100, MinimumLength = 0)]
    public string? Filter { get; init; }

    /// <summary>
    /// The number of items to return in the response.
    /// </summary>
    [Minimum(1)]
    [Maximum(20)]
    public int PageSize { get; init; } = 10;

    /// <summary>
    /// A non-zero integer which is the start index of the entire list of items to return in the response. The combination of <c>page=1</c> and <c>page_size=20</c> returns the first 20 items. The combination of <c>page=2</c> and <c>page_size=20</c> returns the next 20 items.
    /// </summary>
    [Minimum(1)]
    [Maximum(10000000)]
    public int Page { get; init; } = 1;

    /// <summary>
    /// Filters the response by comma separated vault customer IDs (FSS subscriptions only).
    /// </summary>
    [MinLength(1)]
    [MaxLength(10)]
    public IReadOnlyList<string>? CustomerIds { get; init; }
}
