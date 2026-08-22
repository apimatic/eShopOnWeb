using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new UnauthorizedAccessException("The caller is not authenticated.");
        }

        return name;
    }

    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}

internal static class OrderResponseMapper
{
    public static OrderDto ToDto(Order order) => new()
    {
        OrderId = order.Id,
        BuyerId = order.BuyerId,
        Status = order.Status.ToString(),
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Currency = order.Currency,
        ShipTo = new AddressDto
        {
            Street = order.ShipToAddress.Street,
            City = order.ShipToAddress.City,
            State = order.ShipToAddress.State,
            Country = order.ShipToAddress.Country,
            ZipCode = order.ShipToAddress.ZipCode
        },
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Payment = new PaymentStateDto
        {
            PayPalOrderId = order.PayPalOrderId,
            AuthorizationId = order.AuthorizationId,
            AuthorizationStatus = order.AuthorizationStatus,
            AuthorizationExpiration = order.AuthorizationExpiration,
            AuthorizedAmount = order.AuthorizedAmount,
            CaptureId = order.CaptureId,
            CaptureStatus = order.CaptureStatus,
            CapturedAmount = order.CapturedAmount,
            PayPalFee = order.PayPalFee,
            NetAmount = order.NetAmount,
            RemainingRefundableAmount = order.RemainingRefundableAmount(),
            Refunds = order.Refunds.Select(ToDto).ToList()
        }
    };

    public static RefundDto ToDto(OrderRefund refund) => new()
    {
        RefundId = refund.Id,
        PayPalRefundId = refund.PayPalRefundId,
        Amount = refund.Amount,
        Currency = refund.Currency,
        Status = refund.Status,
        IdempotencyKey = refund.IdempotencyKey,
        CreatedAt = refund.CreatedAt
    };

    public static PaymentMethodDto ToDto(ShopperPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        Last4 = method.Last4,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName
    };
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentStateDto
{
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public decimal? AuthorizedAmount { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RemainingRefundableAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public AddressDto ShipTo { get; set; } = new();
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentStateDto Payment { get; set; } = new();
}

public class CardRequestDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BillingAddressRequestDto? BillingAddress { get; set; }
}

public class BillingAddressRequestDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}
