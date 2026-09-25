using System.ComponentModel.DataAnnotations;
using PayPalServerSdk.Core.Validation.Attributes;

namespace PayPalServerSdk.Requests.Vault;

/// <summary>
/// The inputs of the ListCustomerPaymentTokens operation.
/// </summary>
public sealed record ListCustomerPaymentTokensRequest
{
    /// <summary>
    /// A unique identifier representing a specific customer in merchant's/partner's system or records.
    /// </summary>
    [StringLength(36, MinimumLength = 7)]
    [RegularExpression("^[0-9a-zA-Z_-]+$")]
    public required string CustomerId { get; init; }

    /// <summary>
    /// A non-negative, non-zero integer indicating the maximum number of results to return at one time.
    /// </summary>
    [Minimum(1)]
    [Maximum(5)]
    public int PageSize { get; init; } = 5;

    /// <summary>
    /// A non-negative, non-zero integer representing the page of the results.
    /// </summary>
    [Minimum(1)]
    [Maximum(10)]
    public int Page { get; init; } = 1;

    /// <summary>
    /// A boolean indicating total number of items (total_items) and pages (total_pages) are expected to be returned in the response.
    /// </summary>
    public bool TotalRequired { get; init; } = false;
}
