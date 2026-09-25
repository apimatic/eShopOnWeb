using System.ComponentModel.DataAnnotations;
using PayPalServerSdk.Core.Validation.Attributes;

namespace PayPalServerSdk.Requests.Subscriptions;

/// <summary>
/// The inputs of the ListBillingPlans operation.
/// </summary>
public sealed record ListBillingPlansRequest
{
    /// <summary>
    /// Filters the response by a Product ID.
    /// </summary>
    [StringLength(50, MinimumLength = 6)]
    public string? ProductId { get; init; }

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
    [Maximum(100000)]
    public int Page { get; init; } = 1;

    /// <summary>
    /// Indicates whether to show the total count in the response.
    /// </summary>
    public bool TotalRequired { get; init; } = false;

    /// <summary>
    /// The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, name, description and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.
    /// </summary>
    public string Prefer { get; init; } = "return=minimal";
}
