using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipTo { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PayOrderRequest : BaseRequest
{
    public int? PaymentMethodId { get; set; }
    public CardDto? Card { get; set; }
}

public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class RefundOrderRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderDetailsDto Order { get; set; } = new();
}

public class OrderActionResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderDetailsDto Order { get; set; } = new();
}

public class RefundOrderResponse : BaseResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public OrderDetailsDto Order { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public List<OrderDetailsDto> Orders { get; set; } = new();
}

public class OrderDetailsDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? PayPalAuthorizationId { get; set; }
    public string? PayPalAuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; set; }
    public string? PayPalCaptureId { get; set; }
    public string? PayPalCaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<RefundDto> Refunds { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

internal static class OrderDtoMapper
{
    public static OrderDetailsDto ToDto(Order order)
    {
        return new OrderDetailsDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            PaymentStatus = order.PaymentStatus.ToString(),
            Total = order.Total(),
            Currency = order.CurrencyCode,
            PayPalOrderId = order.PayPalOrderId,
            PayPalAuthorizationId = order.PayPalAuthorizationId,
            PayPalAuthorizationStatus = order.PayPalAuthorizationStatus,
            AuthorizationExpirationTime = order.AuthorizationExpirationTime,
            PayPalCaptureId = order.PayPalCaptureId,
            PayPalCaptureStatus = order.PayPalCaptureStatus,
            CapturedAmount = order.CapturedAmount,
            PaypalFee = order.PaypalFee,
            NetAmount = order.NetAmount,
            RefundedAmount = order.RefundedTotal(),
            RemainingRefundable = order.RemainingRefundable(),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Refunds = order.Refunds.Select(r => new RefundDto
            {
                RefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status,
                CreatedAt = r.CreatedAt
            }).ToList()
        };
    }

    public static Address ToAddress(ShippingAddressDto? dto)
    {
        if (dto is null)
        {
            return new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        }

        return new Address(dto.Street, dto.City, dto.State, dto.Country, dto.ZipCode);
    }

    public static CardInput ToCardInput(CardDto card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry)
            || string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new CheckoutException(400, "Card number, expiry, and security code are required.");
        }

        return new CardInput
        {
            Number = new string(card.Number.Where(char.IsDigit).ToArray()),
            Expiry = NormalizeExpiry(card.Expiry),
            SecurityCode = card.SecurityCode.Trim(),
            Name = card.Name,
            BillingAddress = card.BillingAddress is null ? null : new CardBillingAddress
            {
                AddressLine1 = card.BillingAddress.AddressLine1,
                AddressLine2 = card.BillingAddress.AddressLine2,
                AdminArea2 = card.BillingAddress.AdminArea2,
                AdminArea1 = card.BillingAddress.AdminArea1,
                PostalCode = card.BillingAddress.PostalCode,
                CountryCode = string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode)
                    ? "US"
                    : card.BillingAddress.CountryCode.Trim()
            }
        };
    }

    public static string NormalizeExpiry(string expiry)
    {
        var trimmed = expiry.Trim();
        if (trimmed.Length == 7 && trimmed[4] == '-')
        {
            return trimmed;
        }

        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length == 6)
        {
            return $"{digits[2..6]}-{digits[0..2]}";
        }

        if (digits.Length == 4)
        {
            var year = CultureInfo.InvariantCulture.Calendar.ToFourDigitYear(int.Parse(digits[2..4], CultureInfo.InvariantCulture));
            return $"{year:0000}-{digits[0..2]}";
        }

        throw new CheckoutException(400, "Card expiry must be YYYY-MM.");
    }
}
