using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PayPalEndpoints;

/// <summary>Base for PayPal endpoint requests: carries the token-derived caller and the request's
/// cancellation token, both set by the endpoint (never bound from the request body).</summary>
public abstract class PayPalRequestBase : BaseRequest
{
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken Ct { get; set; }
}

internal static class CallerIdentity
{
    /// <summary>The caller's identity (ClaimTypes.Name), as issued into the JWT. Never trust the body.</summary>
    public static string BuyerId(HttpContext http)
    {
        var name = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(name))
            throw new PaymentValidationException("The authenticated token carries no user identity.");
        return name;
    }
}

// ----- Place order -----

public class CreateOrderRequest : PayPalRequestBase
{
    public List<OrderLineItemDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public record OrderLineItemDto(int CatalogItemId, int Quantity);

public record ShippingAddressDto(string? Street, string? City, string? State, string? Country, string? ZipCode);

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

// ----- Card input (pay & save card) -----

public class CardDto
{
    public string Number { get; set; } = string.Empty;
    /// <summary>ISO-8601 expiry <c>YYYY-MM</c>. Alternatively provide <see cref="ExpiryMonth"/>/<see cref="ExpiryYear"/>.</summary>
    public string? Expiry { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToCardDetails()
    {
        if (string.IsNullOrWhiteSpace(Number))
            throw new PaymentValidationException("Card number is required.");
        if (string.IsNullOrWhiteSpace(SecurityCode))
            throw new PaymentValidationException("Card security code is required.");

        var expiry = NormalizeExpiry();
        CardBillingAddress? billing = BillingAddress is null
            ? null
            : new CardBillingAddress(BillingAddress.AddressLine1, BillingAddress.AddressLine2,
                BillingAddress.AdminArea1, BillingAddress.AdminArea2, BillingAddress.PostalCode,
                BillingAddress.CountryCode);

        return new CardDetails(Number.Trim(), expiry, SecurityCode.Trim(), CardholderName, billing);
    }

    private string NormalizeExpiry()
    {
        if (!string.IsNullOrWhiteSpace(Expiry))
        {
            var value = Expiry.Trim();
            // Accept YYYY-MM directly, or MM/YY, MM/YYYY.
            if (System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9]{4}-(0[1-9]|1[0-2])$"))
                return value;
            var slash = value.Split('/', '-');
            if (slash.Length == 2 && int.TryParse(slash[0], out var m) && int.TryParse(slash[1], out var y))
                return BuildExpiry(m, y);
            throw new PaymentValidationException("Card expiry must be in YYYY-MM format.");
        }
        if (ExpiryMonth is int month && ExpiryYear is int year)
            return BuildExpiry(month, year);
        throw new PaymentValidationException("Card expiry is required (YYYY-MM, or expiryMonth + expiryYear).");
    }

    private static string BuildExpiry(int month, int year)
    {
        if (month < 1 || month > 12) throw new PaymentValidationException("Card expiry month must be 1-12.");
        if (year < 100) year += 2000;
        return string.Create(CultureInfo.InvariantCulture, $"{year:D4}-{month:D2}");
    }
}

public record BillingAddressDto(string? AddressLine1, string? AddressLine2, string? AdminArea1,
    string? AdminArea2, string? PostalCode, string? CountryCode);

// ----- Pay / fulfil / cancel / my-orders -----

public class PayOrderRequest : PayPalRequestBase
{
    [JsonIgnore] public int OrderId { get; set; }
    public CardDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class OrderRouteRequest : PayPalRequestBase
{
    [JsonIgnore] public int OrderId { get; set; }
}

public class MyOrdersRequest : PayPalRequestBase { }

public class PaymentStateResponse : BaseResponse
{
    public PaymentStateResponse() { }
    public PaymentStateResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public string? LastError { get; set; }
    public List<OrderLineResponseDto> Items { get; set; } = new();
    public List<RefundStateDto> Refunds { get; set; } = new();
}

public record OrderLineResponseDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);
public record RefundStateDto(string RefundId, decimal Amount, string Status, DateTimeOffset CreatedAt);

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public List<PaymentStateResponse> Orders { get; set; } = new();
}

// ----- Refund -----

public class RefundOrderRequest : PayPalRequestBase
{
    [JsonIgnore] public int OrderId { get; set; }
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

// ----- Saved cards -----

public class SavePaymentMethodRequest : PayPalRequestBase
{
    public CardDto Card { get; set; } = new();
}

public class SavedCardResponse : BaseResponse
{
    public SavedCardResponse() { }
    public SavedCardResponse(Guid correlationId) : base(correlationId) { }
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentMethodsResponse : BaseResponse
{
    public PaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public List<SavedCardResponse> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodRequest : PayPalRequestBase
{
    [JsonIgnore] public int PaymentMethodId { get; set; }
}

// ----- Reconciliation -----

public class ReconciliationRequest : PayPalRequestBase
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<ReconciliationTxnDto> InPayPalOnly { get; set; } = new();
    public List<ReconciliationOrderDto> InEShopOnly { get; set; } = new();
}

public record ReconciliationMatchDto(int OrderId, string EShopStatus, decimal EShopAmount, ReconciliationTxnDto Transaction);
public record ReconciliationTxnDto(string? TransactionId, string? Status, decimal? Amount, string? CurrencyCode, string? InvoiceId, string? InitiationDate, decimal? FeeAmount);
public record ReconciliationOrderDto(int OrderId, string Status, decimal Amount, string Currency, string? CaptureId);

// ----- Mapping -----

internal static class PaymentMapper
{
    public static PaymentStateResponse ToResponse(OrderPaymentView v, Guid correlationId) => new(correlationId)
    {
        OrderId = v.OrderId,
        OrderDate = v.OrderDate,
        Total = v.Total,
        Currency = v.Currency,
        Status = v.Status.ToString(),
        PayPalOrderId = v.PayPalOrderId,
        AuthorizationId = v.AuthorizationId,
        AuthorizationExpiresAt = v.AuthorizationExpiresAt,
        CaptureId = v.CaptureId,
        CapturedAmount = v.CapturedAmount,
        PayPalFee = v.PayPalFee,
        NetAmount = v.NetAmount,
        RefundedAmount = v.RefundedAmount,
        LastError = v.LastError,
        Items = v.Items.Select(i => new OrderLineResponseDto(i.CatalogItemId, i.ProductName, i.UnitPrice, i.Units)).ToList(),
        Refunds = v.Refunds.Select(r => new RefundStateDto(r.RefundId, r.Amount, r.Status, r.CreatedAt)).ToList(),
    };

    public static ReconciliationTxnDto ToDto(PayPalTransaction t)
        => new(t.TransactionId, t.Status, t.Amount, t.CurrencyCode, t.InvoiceId, t.InitiationDate, t.FeeAmount);
}
