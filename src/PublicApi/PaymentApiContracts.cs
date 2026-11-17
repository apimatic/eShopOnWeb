using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi;

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CardRequest
{
    public string? Number { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public ShippingAddressRequest? BillingAddress { get; set; }
}

public class PayOrderRequest
{
    public int? PaymentMethodId { get; set; }
    public CardRequest? Card { get; set; }
}

public class RefundOrderRequest
{
    public int? AmountCents { get; set; }
    public string? IdempotencyKey { get; set; }
}

public static class PaymentApiMappings
{
    public static CreateOrderCommand ToCommand(this CreateOrderRequest request, string buyerId)
    {
        return new CreateOrderCommand
        {
            BuyerId = buyerId,
            Items = request.Items
                .Select(i => new CreateOrderItemCommand(i.CatalogItemId, i.Quantity))
                .ToList(),
            ShippingAddress = request.ShippingAddress.ToCommand()
        };
    }

    public static PayOrderCommand ToCommand(this PayOrderRequest request, string buyerId, int orderId)
    {
        return new PayOrderCommand
        {
            BuyerId = buyerId,
            OrderId = orderId,
            PaymentMethodId = request.PaymentMethodId,
            Card = request.Card.ToCommand()
        };
    }

    public static SaveCardCommand ToCommand(this CardRequest request, string buyerId)
    {
        return new SaveCardCommand
        {
            BuyerId = buyerId,
            Card = request.ToCommand() ?? new CardCommand()
        };
    }

    public static RefundOrderCommand ToCommand(this RefundOrderRequest request, int orderId)
    {
        return new RefundOrderCommand
        {
            OrderId = orderId,
            AmountCents = request.AmountCents,
            IdempotencyKey = request.IdempotencyKey ?? string.Empty
        };
    }

    private static ShippingAddressCommand? ToCommand(this ShippingAddressRequest? request)
    {
        if (request == null)
        {
            return null;
        }

        return new ShippingAddressCommand
        {
            Street = request.Street,
            City = request.City,
            State = request.State,
            Country = request.Country,
            ZipCode = request.ZipCode
        };
    }

    private static CardCommand? ToCommand(this CardRequest? request)
    {
        if (request == null)
        {
            return null;
        }

        return new CardCommand
        {
            Number = request.Number,
            ExpirationMonth = request.ExpirationMonth,
            ExpirationYear = request.ExpirationYear,
            SecurityCode = request.SecurityCode,
            Name = request.Name,
            BillingAddress = request.BillingAddress.ToCommand()
        };
    }
}
