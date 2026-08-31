using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PlaceOrderRequest
{
    [Required, MinLength(1), MaxLength(100)]
    public List<OrderLineRequest> Items { get; set; } = new();

    [Required]
    public ShippingAddressRequest ShippingAddress { get; set; } = new();
}

public sealed class OrderLineRequest
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; set; }

    [Range(1, 100)]
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    [Required, MaxLength(180)] public string Street { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string City { get; set; } = string.Empty;
    [MaxLength(60)] public string State { get; set; } = string.Empty;
    [Required, MaxLength(90)] public string Country { get; set; } = string.Empty;
    [Required, MaxLength(18)] public string ZipCode { get; set; } = string.Empty;
}

public class CardRequest : IValidatableObject
{
    [Required] public string Number { get; set; } = string.Empty;
    [Required, RegularExpression(@"^\d{4}-(0[1-9]|1[0-2])$")] public string Expiry { get; set; } = string.Empty;
    [Required, RegularExpression(@"^\d{3,4}$")] public string SecurityCode { get; set; } = string.Empty;
    [Required, StringLength(300, MinimumLength = 2)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(300)] public string AddressLine1 { get; set; } = string.Empty;
    [MaxLength(300)] public string? AddressLine2 { get; set; }
    [Required, MaxLength(120)] public string City { get; set; } = string.Empty;
    [Required, MaxLength(300)] public string State { get; set; } = string.Empty;
    [Required, MaxLength(60)] public string PostalCode { get; set; } = string.Empty;
    [Required, RegularExpression(@"^[A-Za-z]{2}$")] public string CountryCode { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var digits = NormalizeNumber();
        if (digits.Length is < 12 or > 19 || digits.Any(c => !char.IsDigit(c)))
        {
            yield return new ValidationResult("Card number must contain 12 to 19 digits.", new[] { nameof(Number) });
        }

        if (DateTime.TryParseExact(Expiry + "-01", "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var month) &&
            month.AddMonths(1) <= DateTime.UtcNow.Date)
        {
            yield return new ValidationResult("Card expiry must be in the future.", new[] { nameof(Expiry) });
        }
    }

    public CardDetails ToCardDetails() => new(
        NormalizeNumber(), Expiry, SecurityCode, Name, AddressLine1, AddressLine2, City, State, PostalCode, CountryCode);

    private string NormalizeNumber() => Number.Replace(" ", string.Empty).Replace("-", string.Empty);
}

public sealed class PayOrderRequest : IValidatableObject
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if ((Card == null) == (PaymentMethodId == null))
        {
            yield return new ValidationResult("Supply exactly one of card or paymentMethodId.");
        }
        if (PaymentMethodId <= 0)
        {
            yield return new ValidationResult("paymentMethodId must be positive.", new[] { nameof(PaymentMethodId) });
        }
    }
}

public sealed class SavePaymentMethodRequest : CardRequest
{
}

public sealed class RefundOrderRequest
{
    [Range(typeof(decimal), "0.01", "9999999999999999")]
    public decimal? Amount { get; set; }

    [Required, StringLength(78, MinimumLength = 1)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record CreateOrderResponse(int OrderId, decimal Total, string PaymentStatus, string FulfilmentStatus);
public sealed record PayOrderResponse(int OrderId, string PaymentStatus, string PayPalOrderId, string AuthorizationId, decimal AuthorizedAmount, string Currency, DateTimeOffset? ExpiresAt);
public sealed record FulfilOrderResponse(int OrderId, string PaymentStatus, string FulfilmentStatus, string CaptureId, decimal CapturedAmount, decimal PayPalFee, decimal NetProceeds, string Currency);
public sealed record CancelOrderResponse(int OrderId, string PaymentStatus, string FulfilmentStatus);
public sealed record RefundOrderResponse(string RefundId, int OrderId, string Status, decimal Amount, string Currency);
public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string Last4, string Expiry, string? CardholderName);
public sealed record OrderLineResponse(int CatalogItemId, string ProductName, int Quantity, decimal UnitPrice);
public sealed record RefundResponse(string? RefundId, string Status, decimal Amount, string Currency, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public sealed record OrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string? Currency,
    string PaymentStatus,
    string FulfilmentStatus,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetProceeds,
    IReadOnlyList<OrderLineResponse> Items,
    IReadOnlyList<RefundResponse> Refunds)
{
    public static OrderResponse FromOrder(Order order) => new(
        order.Id,
        order.OrderDate,
        order.Total(),
        order.PaymentCurrency,
        order.PaymentStatus.ToString(),
        order.FulfilmentStatus.ToString(),
        order.PayPalAuthorizationId,
        order.PayPalAuthorizationStatus,
        order.AuthorizationExpiresAt,
        order.PayPalCaptureId,
        order.PayPalCaptureStatus,
        order.CapturedAmount,
        order.PayPalFee,
        order.NetProceeds,
        order.OrderItems.Select(x => new OrderLineResponse(x.ItemOrdered.CatalogItemId, x.ItemOrdered.ProductName, x.Units, x.UnitPrice)).ToList(),
        order.Refunds.Select(x => new RefundResponse(x.PayPalRefundId, x.PayPalStatus ?? x.Status.ToString(), x.Amount, x.Currency, x.CreatedAt, x.CompletedAt)).ToList());
}

public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ReconciliationEntry> Entries);

public sealed record ReconciliationEntry(
    string MatchStatus,
    int? OrderId,
    string LocalRecordType,
    string? LocalPayPalId,
    string? PayPalTransactionId,
    string? PayPalReferenceId,
    string? EventCode,
    string? TransactionStatus,
    DateTimeOffset? TransactionTime,
    decimal? Amount,
    string? Currency,
    decimal? Fee);
