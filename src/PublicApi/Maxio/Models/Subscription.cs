using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// Maxio Advanced Billing Subscription, per the OpenAPI spec schema <c>Subscription</c>.
/// </summary>
public class Subscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long BalanceInCents { get; set; }
    public long TotalRevenueInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public int ProductVersionNumber { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? TrialStartedAt { get; set; }
    public DateTime? TrialEndedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CancellationMessage { get; set; }
    public string? CancellationMethod { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public DateTime? CanceledAt { get; set; }
    public DateTime? CurrentPeriodStartedAt { get; set; }
    public string? PreviousState { get; set; }
    public int? SignupPaymentId { get; set; }
    public string? SignupRevenue { get; set; }
    public DateTime? DelayedCancelAt { get; set; }
    public string? CouponCode { get; set; }
    public string? SnapDay { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public Customer? Customer { get; set; }
    public Product? Product { get; set; }
    public string? PaymentType { get; set; }
    public string? ReferralCode { get; set; }
    public int? NextProductId { get; set; }
    public string? NextProductHandle { get; set; }
    public int? CouponUseCount { get; set; }
    public int? CouponUsesAllowed { get; set; }
    public string? ReasonCode { get; set; }
    public DateTime? AutomaticallyResumeAt { get; set; }
    public string[]? CouponCodes { get; set; }
    public int? OfferId { get; set; }
    public int? PayerId { get; set; }
    public long? CurrentBillingAmountInCents { get; set; }
    public int? ProductPricePointId { get; set; }
    public string? ProductPricePointType { get; set; }
    public int? NextProductPricePointId { get; set; }
    public int? NetTerms { get; set; }
    public int? StoredCredentialTransactionId { get; set; }
    public string? Reference { get; set; }
    public DateTime? OnHoldAt { get; set; }
    public bool PrepaidDunning { get; set; }
    public bool? DunningCommunicationDelayEnabled { get; set; }
    public string? DunningCommunicationDelayTimeZone { get; set; }
    public bool? ReceivesInvoiceEmails { get; set; }
    public string? Locale { get; set; }
    public string? Currency { get; set; }
    public DateTime? ScheduledCancellationAt { get; set; }
    public long CreditBalanceInCents { get; set; }
    public long PrepaymentBalanceInCents { get; set; }
    public string? SelfServicePageToken { get; set; }
}
