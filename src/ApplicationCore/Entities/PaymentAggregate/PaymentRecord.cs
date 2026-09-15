using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public sealed class PaymentRecord : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

    private PaymentRecord() { }

    public PaymentRecord(int orderId, string currency, decimal orderAmount)
    {
        OrderId = orderId;
        Currency = currency;
        OrderAmount = orderAmount;
        CreateRequestId = Guid.NewGuid().ToString("N");
        AuthorizeRequestId = Guid.NewGuid().ToString("N");
        CaptureRequestId = Guid.NewGuid().ToString("N");
        VoidRequestId = Guid.NewGuid().ToString("N");
    }

    public int OrderId { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public decimal OrderAmount { get; private set; }
    public string State { get; private set; } = PaymentStates.AwaitingPayment;
    public string CreateRequestId { get; private set; } = string.Empty;
    public string AuthorizeRequestId { get; private set; } = string.Empty;
    public string CaptureRequestId { get; private set; } = string.Empty;
    public string VoidRequestId { get; private set; } = string.Empty;
    public string? ReauthorizeRequestId { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public int AuthorizationGeneration { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? MerchantNet { get; private set; }
    public DateTimeOffset? CaptureCreatedAt { get; private set; }
    public string? LastProviderError { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void RecordPayPalOrder(string id, string? status)
    {
        PayPalOrderId = id;
        PayPalOrderStatus = status;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordAuthorization(string id, string? status, decimal amount,
        DateTimeOffset? createdAt, DateTimeOffset? expiresAt, bool renewed = false)
    {
        AuthorizationId = id;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationCreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        AuthorizationExpiresAt = expiresAt;
        if (renewed) AuthorizationGeneration++;
        State = PaymentStates.Authorized;
        LastProviderError = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public string ReserveReauthorization()
    {
        ReauthorizeRequestId ??= Guid.NewGuid().ToString("N");
        UpdatedAt = DateTimeOffset.UtcNow;
        return ReauthorizeRequestId;
    }

    public void RecordCapture(string id, string? status, decimal amount, decimal? fee, decimal? net,
        DateTimeOffset? createdAt)
    {
        CaptureId = id;
        CaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        MerchantNet = net;
        CaptureCreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        State = PaymentStates.Captured;
        LastProviderError = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordVoid(string? status)
    {
        AuthorizationStatus = status;
        State = PaymentStates.Voided;
        LastProviderError = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ReserveRefund() => UpdatedAt = DateTimeOffset.UtcNow;

    public void RecordChallenge()
    {
        State = PaymentStates.PayerActionRequired;
        LastProviderError = "PayPal requires browser approval; this headless card flow cannot continue.";
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordFailure(string safeMessage)
    {
        LastProviderError = safeMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public static class PaymentStates
{
    public const string AwaitingPayment = "AwaitingPayment";
    public const string Authorized = "Authorized";
    public const string Captured = "Captured";
    public const string Voided = "Voided";
    public const string PayerActionRequired = "PayerActionRequired";
}
