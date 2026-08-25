using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentInfo
{
#pragma warning disable CS8618
    private PaymentInfo() { }
#pragma warning restore CS8618

    public PaymentInfo(string paypalOrderId, string authorizationId,
        string authorizationStatus, string? expirationTime)
    {
        PayPalOrderId = paypalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpirationTime = expirationTime;
    }

    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public string? AuthorizationExpirationTime { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string RefundsJson { get; private set; } = "[]";

    public IReadOnlyList<OrderRefund> GetRefunds() =>
        JsonSerializer.Deserialize<List<OrderRefund>>(RefundsJson) ?? new List<OrderRefund>();

    public decimal TotalRefunded() =>
        GetRefunds()
            .Where(r => r.Status is not ("FAILED" or "CANCELLED"))
            .Sum(r => r.Amount);

    internal void RecordCapture(string captureId, decimal capturedAmount, decimal fee, decimal net, string captureStatus)
    {
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        CaptureStatus = captureStatus;
    }

    internal void UpdateAuthorization(string newId, string newStatus, string? newExpiry)
    {
        AuthorizationId = newId;
        AuthorizationStatus = newStatus;
        AuthorizationExpirationTime = newExpiry;
    }

    internal void AddRefund(OrderRefund refund)
    {
        var list = GetRefunds().ToList();
        list.Add(refund);
        RefundsJson = JsonSerializer.Serialize(list);
    }
}
