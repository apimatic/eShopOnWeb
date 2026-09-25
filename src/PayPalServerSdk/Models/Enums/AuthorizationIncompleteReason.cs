using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The reason why the authorized status is <c>PENDING</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AuthorizationIncompleteReason>))]
public sealed record AuthorizationIncompleteReason : OpenStringEnum<AuthorizationIncompleteReason>
{
    private AuthorizationIncompleteReason(string value) : base(value)
    {
    }

    /// <summary>
    /// Authorization is pending manual review.
    /// </summary>
    public static readonly AuthorizationIncompleteReason PendingReview = new("PENDING_REVIEW");

    /// <summary>
    /// Risk Filter set by the payee failed for the transaction.
    /// </summary>
    public static readonly AuthorizationIncompleteReason DeclinedByRiskFraudFilters = new("DECLINED_BY_RISK_FRAUD_FILTERS");

    public TResult Match<TResult>(Func<TResult> onPendingReview,
        Func<TResult> onDeclinedByRiskFraudFilters,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == PendingReview => onPendingReview(),
            _ when this == DeclinedByRiskFraudFilters => onDeclinedByRiskFraudFilters(),
            _ => otherwise(Value)
        };

    public void Match(Action onPendingReview, Action onDeclinedByRiskFraudFilters, Action<string> otherwise)
    {
        if (this == PendingReview) onPendingReview();
        else if (this == DeclinedByRiskFraudFilters) onDeclinedByRiskFraudFilters();
        else otherwise(Value);
    }
}
