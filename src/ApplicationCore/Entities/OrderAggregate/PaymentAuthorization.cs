using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentAuthorization : BaseEntity
{
#pragma warning disable CS8618
    private PaymentAuthorization() { }
#pragma warning restore CS8618

    internal PaymentAuthorization(string sourceType, int? paymentMethodId)
    {
        SourceType = sourceType;
        PaymentMethodId = paymentMethodId;
        ExternalReference = $"eshop-{Guid.NewGuid():N}";
        CreateOrderRequestId = $"eshop-order-{Guid.NewGuid():N}";
        AuthorizeRequestId = $"eshop-authorize-{Guid.NewGuid():N}";
        ReauthorizeRequestId = $"eshop-reauthorize-{Guid.NewGuid():N}";
        IsCurrent = true;
    }

    public int OrderPaymentId { get; private set; }
    public string SourceType { get; private set; }
    public int? PaymentMethodId { get; private set; }
    public string ExternalReference { get; private set; }
    public string CreateOrderRequestId { get; private set; }
    public string AuthorizeRequestId { get; private set; }
    public string ReauthorizeRequestId { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public string? Currency { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public bool IsCurrent { get; private set; }

    public void RecordOrder(string id, string status)
    {
        PayPalOrderId = id;
        PayPalOrderStatus = status;
    }

    public void RecordAuthorization(string orderStatus, string id, string status, decimal amount,
        string currency, DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        PayPalOrderStatus = orderStatus;
        PayPalAuthorizationId = id;
        PayPalAuthorizationStatus = status;
        AuthorizedAmount = amount;
        Currency = currency;
        AuthorizedAt = createdAt ?? DateTimeOffset.UtcNow;
        ExpiresAt = expiresAt;
    }

    public void RecordReauthorization(string id, string status, decimal amount, string currency,
        DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        PayPalAuthorizationId = id;
        PayPalAuthorizationStatus = status;
        AuthorizedAmount = amount;
        Currency = currency;
        AuthorizedAt = createdAt ?? DateTimeOffset.UtcNow;
        ExpiresAt = expiresAt;
        ReauthorizeRequestId = $"eshop-reauthorize-{Guid.NewGuid():N}";
    }

    internal void MakeHistorical() => IsCurrent = false;
    public void RecordStatus(string status) => PayPalAuthorizationStatus = status;
}
