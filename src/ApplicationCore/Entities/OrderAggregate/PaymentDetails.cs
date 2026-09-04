using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Owned reference type holding the state the payment provider owns for this order:
/// provider order id, current authorization (id/status/expiry/amount) and the capture
/// (id/status/gross/fee/net). Ids here are what let later requests act on the money.
/// No card data — ever.
/// </summary>
public class PaymentDetails
{
    private PaymentDetails() { }

    public PaymentDetails(string providerName,
        string providerOrderId,
        string? authorizationId,
        string authorizationStatus,
        decimal authorizedAmount,
        string currencyCode,
        DateTimeOffset? authorizationExpirationTime,
        DateTimeOffset? authorizationCreatedTime,
        string? networkTransactionReference)
    {
        ProviderName = providerName;
        ProviderOrderId = providerOrderId;
        AuthorizationId = authorizationId ?? string.Empty;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        CurrencyCode = currencyCode;
        AuthorizationExpirationTime = authorizationExpirationTime;
        AuthorizationCreatedTime = authorizationCreatedTime;
        NetworkTransactionReference = networkTransactionReference ?? string.Empty;
    }

    public string ProviderName { get; private set; } = string.Empty;
    public string ProviderOrderId { get; private set; } = string.Empty;

    /// <summary>Empty while an interrupted authorization attempt is still awaiting recovery.</summary>
    public string AuthorizationId { get; private set; } = string.Empty;
    public string AuthorizationStatus { get; private set; } = string.Empty;
    public decimal AuthorizedAmount { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }
    public DateTimeOffset? AuthorizationCreatedTime { get; private set; }
    public string NetworkTransactionReference { get; private set; } = string.Empty;

    /// <summary>Set when the hold was funded by a saved card; enables renewal without raw card data.</summary>
    public string UsedVaultTokenId { get; private set; } = string.Empty;

    /// <summary>
    /// Unique provider invoice reference for this hold (this merchant account enforces
    /// invoice-id uniqueness, so it carries an attempt nonce). Replays reuse it; a renewed
    /// hold gets a fresh one.
    /// </summary>
    public string InvoiceReference { get; private set; } = string.Empty;

    public void NoteInvoiceReference(string invoiceReference)
    {
        InvoiceReference = invoiceReference;
    }

    public string CaptureId { get; private set; } = string.Empty;
    public string CaptureStatus { get; private set; } = string.Empty;
    public decimal? CapturedAmount { get; private set; }
    public decimal? FeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    /// <summary>
    /// The provider order was created but the authorization never confirmed (crashed/interrupted
    /// attempt). AuthorizationId stays empty until the pay replay settles it.
    /// </summary>
    public static PaymentDetails ForPendingProviderOrder(string providerName, string providerOrderId, string currencyCode, decimal amount)
    {
        return new PaymentDetails(providerName, providerOrderId, null, AuthorizationStatuses.Created, amount, currencyCode, null, null, null);
    }

    public bool HasPendingAuthorizationToRecover =>
        !string.IsNullOrEmpty(ProviderOrderId) && string.IsNullOrEmpty(AuthorizationId) && !IsCaptured;

    public void NoteVaultTokenId(string? vaultTokenId)
    {
        if (!string.IsNullOrEmpty(vaultTokenId))
        {
            UsedVaultTokenId = vaultTokenId;
        }
    }

    public bool IsCaptured => !string.IsNullOrEmpty(CaptureId);
    public bool IsVoided => AuthorizationStatus == AuthorizationStatuses.Voided;
    public bool AuthorizationExpired =>
        !string.IsNullOrEmpty(AuthorizationId) &&
        AuthorizationExpirationTime.HasValue &&
        !IsCaptured &&
        !IsVoided &&
        AuthorizationExpirationTime.Value <= DateTimeOffset.UtcNow;

    public void RenewAuthorization(string newAuthorizationId, string newAuthorizationStatus, decimal newAmount, DateTimeOffset? newExpirationTime, string? newProviderOrderId = null, string? newNetworkTransactionReference = null)
    {
        AuthorizationId = newAuthorizationId;
        AuthorizationStatus = newAuthorizationStatus;
        AuthorizedAmount = newAmount;
        AuthorizationExpirationTime = newExpirationTime;
        if (!string.IsNullOrEmpty(newProviderOrderId))
        {
            ProviderOrderId = newProviderOrderId;
        }
        if (!string.IsNullOrEmpty(newNetworkTransactionReference))
        {
            NetworkTransactionReference = newNetworkTransactionReference;
        }
    }

    public void RecordCapture(string captureId, string captureStatus, decimal grossAmount, decimal? feeAmount, decimal? netAmount, DateTimeOffset capturedAt)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = grossAmount;
        FeeAmount = feeAmount;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        AuthorizationStatus = AuthorizationStatuses.Captured;
    }

    public void MarkVoided()
    {
        AuthorizationStatus = AuthorizationStatuses.Voided;
    }

    public void RecordRefundSummary(string refundStatus)
    {
        if (refundStatus == RefundStatuses.Completed)
        {
            CaptureStatus = CaptureStatuses.Refunded;
        }
    }
}

public static class AuthorizationStatuses
{
    public const string Created = "CREATED";
    public const string Captured = "CAPTURED";
    public const string Denied = "DENIED";
    public const string PartiallyCaptured = "PARTIALLY_CAPTURED";
    public const string Voided = "VOIDED";
    public const string Pending = "PENDING";
}

public static class CaptureStatuses
{
    public const string Completed = "COMPLETED";
    public const string Declined = "DECLINED";
    public const string PartiallyRefunded = "PARTIALLY_REFUNDED";
    public const string Pending = "PENDING";
    public const string Refunded = "REFUNDED";
    public const string Failed = "FAILED";
}

public static class RefundStatuses
{
    public const string Cancelled = "CANCELLED";
    public const string Failed = "FAILED";
    public const string Pending = "PENDING";
    public const string Completed = "COMPLETED";
}
