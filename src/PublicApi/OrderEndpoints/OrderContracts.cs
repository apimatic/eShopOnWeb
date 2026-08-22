using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderResponse
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public System.DateTimeOffset OrderDate { get; set; }
    public AddressResponse? ShipToAddress { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
    public PaymentResponse? Payment { get; set; }

    public static OrderResponse From(Order order)
    {
        return new OrderResponse
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = order.Currency,
            OrderDate = order.OrderDate,
            ShipToAddress = order.ShipToAddress is null ? null : new AddressResponse
            {
                Street = order.ShipToAddress.Street,
                City = order.ShipToAddress.City,
                State = order.ShipToAddress.State,
                Country = order.ShipToAddress.Country,
                ZipCode = order.ShipToAddress.ZipCode
            },
            Items = order.OrderItems.Select(i => new OrderItemResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = new PaymentResponse
            {
                PayPalOrderId = order.PayPalOrderId,
                AuthorizationId = order.PayPalAuthorizationId,
                AuthorizationStatus = order.PayPalAuthorizationStatus,
                AuthorizationExpiration = order.AuthorizationExpiration,
                AuthorizedAmount = order.AuthorizedAmount,
                CaptureId = order.PayPalCaptureId,
                CaptureStatus = order.PayPalCaptureStatus,
                CapturedAmount = order.CapturedAmount,
                PaypalFee = order.PaypalFee,
                NetProceeds = order.NetProceeds,
                RefundedAmount = order.RefundedAmount,
                RemainingRefundable = order.RemainingRefundable,
                Refunds = order.Refunds.Select(r => new RefundResponse
                {
                    RefundId = r.Id,
                    PayPalRefundId = r.PayPalRefundId,
                    Status = r.PayPalRefundStatus,
                    Amount = r.Amount,
                    Currency = r.Currency,
                    IdempotencyKey = r.IdempotencyKey,
                    CreatedAt = r.CreatedAt
                }).ToList()
            }
        };
    }
}

public class AddressResponse
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class PaymentResponse
{
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public System.DateTimeOffset? AuthorizationExpiration { get; set; }
    public decimal? AuthorizedAmount { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetProceeds { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<RefundResponse> Refunds { get; set; } = new();
}

public class RefundResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public System.DateTimeOffset CreatedAt { get; set; }
}

public class AddressRequest
{
    public string Street { get; set; } = "123 Main St.";
    public string City { get; set; } = "Kent";
    public string State { get; set; } = "OH";
    public string Country { get; set; } = "USA";
    public string ZipCode { get; set; } = "44240";

    public Address ToAddress() => new(Street, City, State, Country, ZipCode);
}

public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BillingAddressRequest? BillingAddress { get; set; }

    public CardPaymentSource ToCardPaymentSource()
    {
        return new CardPaymentSource
        {
            Number = SanitizeDigits(Number),
            Expiry = Expiry?.Trim() ?? string.Empty,
            SecurityCode = SecurityCode?.Trim() ?? string.Empty,
            Name = Name?.Trim() ?? string.Empty,
            BillingAddress = BillingAddress is null ? null : new CardBillingAddress
            {
                CountryCode = BillingAddress.CountryCode,
                AddressLine1 = BillingAddress.AddressLine1,
                AddressLine2 = BillingAddress.AddressLine2,
                AdminArea2 = BillingAddress.AdminArea2,
                AdminArea1 = BillingAddress.AdminArea1,
                PostalCode = BillingAddress.PostalCode
            }
        };
    }

    private static string SanitizeDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(char.IsDigit).ToArray());
    }
}

public class BillingAddressRequest
{
    public string CountryCode { get; set; } = "US";
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
}
