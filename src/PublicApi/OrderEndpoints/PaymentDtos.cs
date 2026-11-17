using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class AddressRequest
{
    public string? Street { get; set; }
    public string? Line1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
    public string? PostalCode { get; set; }

    public Address ToAddress()
    {
        return new Address(
            FirstNonEmpty(Street, Line1) ?? "1 Main Street",
            FirstNonEmpty(City) ?? "Seattle",
            FirstNonEmpty(State, Region) ?? "WA",
            FirstNonEmpty(Country) ?? "USA",
            FirstNonEmpty(ZipCode, PostalCode) ?? "98101");
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}

public class CardRequest : BaseRequest
{
    public string? Number { get; set; }
    public string? CardNumber { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvv { get; set; }
    public string? Name { get; set; }
    public string? CardholderName { get; set; }
    public AddressRequest? Address { get; set; }
    public AddressRequest? BillingAddress { get; set; }

    public bool HasFlatCardFields =>
        !string.IsNullOrWhiteSpace(FirstNonEmpty(Number, CardNumber)) ||
        ExpirationMonth.HasValue ||
        ExpirationYear.HasValue;

    public CardDetails ToCardDetails()
    {
        var number = FirstNonEmpty(Number, CardNumber);
        var name = FirstNonEmpty(Name, CardholderName);
        var billingAddress = Address ?? BillingAddress;

        return new CardDetails
        {
            Number = number ?? string.Empty,
            Name = name ?? string.Empty,
            ExpirationMonth = ExpirationMonth ?? 0,
            ExpirationYear = ExpirationYear ?? 0,
            SecurityCode = FirstNonEmpty(SecurityCode, Cvv),
            Line1 = billingAddress is null ? null : FirstNonEmpty(billingAddress.Street, billingAddress.Line1),
            City = billingAddress?.City,
            Region = billingAddress is null ? null : FirstNonEmpty(billingAddress.State, billingAddress.Region),
            PostalCode = billingAddress is null ? null : FirstNonEmpty(billingAddress.ZipCode, billingAddress.PostalCode),
            Country = billingAddress?.Country
        };
    }

    protected static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
    public int? Units { get; set; }

    public int EffectiveQuantity => Quantity > 0 ? Quantity : Units ?? 0;
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequest>? Items { get; set; }
    public List<OrderItemRequest>? BasketItems { get; set; }
    public AddressRequest? ShipToAddress { get; set; }
    public AddressRequest? ShippingAddress { get; set; }
}

public class PayOrderRequest : CardRequest
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class CreatePaymentMethodRequest : CardRequest
{
    public CardRequest? Card { get; set; }
}

public class RefundOrderRequest : BaseRequest
{
    public decimal? Amount { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? PictureUri { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }

    public static OrderItemDto From(OrderItem item)
    {
        return new OrderItemDto
        {
            CatalogItemId = item.ItemOrdered.CatalogItemId,
            ProductName = item.ItemOrdered.ProductName,
            PictureUri = item.ItemOrdered.PictureUri,
            UnitPrice = item.UnitPrice,
            Quantity = item.Units,
            LineTotal = item.UnitPrice * item.Units
        };
    }
}

public class PaymentDto
{
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public decimal? AuthorizedAmount { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureState { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? Fee { get; set; }
    public decimal? NetProceeds { get; set; }
    public decimal RefundedAmount { get; set; }
    public string? FailureMessage { get; set; }
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundDto From(OrderRefund refund)
    {
        return new RefundDto
        {
            RefundId = refund.FinixRefundId,
            OrderId = refund.OrderId,
            Amount = refund.Amount,
            Currency = refund.Currency,
            State = refund.State,
            IdempotencyKey = refund.IdempotencyKey,
            CreatedAt = refund.CreatedAt
        };
    }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public AddressRequest ShipToAddress { get; set; } = new();
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto Payment { get; set; } = new();
    public bool IsFulfilled { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static OrderDto From(OrderWithRefunds details)
    {
        var order = details.Order;
        return new OrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            PaymentStatus = order.PaymentStatus,
            Total = order.Total(),
            Currency = order.PaymentCurrency,
            ShipToAddress = new AddressRequest
            {
                Street = order.ShipToAddress.Street,
                City = order.ShipToAddress.City,
                State = order.ShipToAddress.State,
                Country = order.ShipToAddress.Country,
                ZipCode = order.ShipToAddress.ZipCode
            },
            Items = order.OrderItems.Select(OrderItemDto.From).ToList(),
            Payment = new PaymentDto
            {
                Status = order.PaymentStatus,
                Currency = order.PaymentCurrency,
                AuthorizationId = order.FinixAuthorizationId,
                AuthorizationState = order.AuthorizationState,
                AuthorizedAmount = order.AuthorizedAmount,
                CaptureId = order.FinixCaptureId,
                CaptureState = order.CaptureState,
                CapturedAmount = order.CapturedAmount,
                Fee = order.FinixFee,
                NetProceeds = order.NetProceeds,
                RefundedAmount = order.RefundedAmount,
                FailureMessage = order.PaymentFailureMessage
            },
            IsFulfilled = order.IsFulfilled,
            FulfilledAt = order.FulfilledAt,
            CanceledAt = order.CanceledAt,
            Refunds = details.Refunds.Select(RefundDto.From).ToList()
        };
    }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(SavedPaymentMethod method)
    {
        return new PaymentMethodDto
        {
            PaymentMethodId = method.Id,
            Brand = method.Brand,
            Last4 = method.LastFour,
            ExpirationMonth = method.ExpirationMonth,
            ExpirationYear = method.ExpirationYear,
            Name = method.Name,
            CreatedAt = method.CreatedAt
        };
    }
}

public class PaymentMethodListResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundResponse From(OrderRefund refund)
    {
        return new RefundResponse
        {
            RefundId = refund.FinixRefundId,
            OrderId = refund.OrderId,
            Amount = refund.Amount,
            Currency = refund.Currency,
            State = refund.State,
            IdempotencyKey = refund.IdempotencyKey,
            CreatedAt = refund.CreatedAt
        };
    }
}
