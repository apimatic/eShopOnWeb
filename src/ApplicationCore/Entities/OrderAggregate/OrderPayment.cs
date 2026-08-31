using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
    private readonly List<PaymentRefund> _refunds = new();
    private OrderPayment() { }
    public OrderPayment(string currency, decimal authorizedAmount, string paypalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset authorizedAt, DateTimeOffset expiresAt)
    {
        Currency = currency; AuthorizedAmount = authorizedAmount; PayPalOrderId = paypalOrderId;
        AuthorizationId = authorizationId; AuthorizationStatus = authorizationStatus;
        AuthorizedAt = authorizedAt; OriginalAuthorizedAt = authorizedAt; AuthorizationExpiresAt = expiresAt;
    }
    public int OrderId { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public decimal AuthorizedAmount { get; private set; }
    public string PayPalOrderId { get; private set; } = string.Empty;
    public string AuthorizationId { get; private set; } = string.Empty;
    public string AuthorizationStatus { get; private set; } = string.Empty;
    public DateTimeOffset AuthorizedAt { get; private set; }
    public DateTimeOffset OriginalAuthorizedAt { get; private set; }
    public DateTimeOffset AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();
    public void RecordReauthorization(string id, string status, DateTimeOffset at, DateTimeOffset expiresAt) { AuthorizationId = id; AuthorizationStatus = status; AuthorizedAt = at; AuthorizationExpiresAt = expiresAt; }
    public void RecordCapture(string id, string status, decimal amount, decimal? fee, decimal? net, DateTimeOffset at) { CaptureId = id; CaptureStatus = status; CapturedAmount = amount; PayPalFee = fee; NetAmount = net; CapturedAt = at; AuthorizationStatus = "CAPTURED"; }
    public void RecordVoid(string status) => AuthorizationStatus = status;
    public PaymentRefund RecordRefund(string key, string id, string status, decimal amount, DateTimeOffset at)
    {
        var existing = _refunds.SingleOrDefault(x => x.IdempotencyKey == key); if (existing != null) return existing;
        if (CapturedAmount == null || amount <= 0 || RefundedAmount + amount > CapturedAmount.Value) throw new InvalidOperationException("The refund exceeds the unrefunded captured amount.");
        var refund = new PaymentRefund(key, id, status, amount, at); _refunds.Add(refund); RefundedAmount += amount; return refund;
    }
}
