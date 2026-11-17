using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class AddressRequest
{
    public string? Line1 { get; set; }
    public string? Street { get; set; }
    public string? AddressLine1 { get; set; }
    [JsonPropertyName("line_1")] public string? Line1Snake { get; set; }
    public string? Line2 { get; set; }
    public string? AddressLine2 { get; set; }
    [JsonPropertyName("line_2")] public string? Line2Snake { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? ZipCode { get; set; }
    public string? Postal { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCodeSnake { get; set; }
    public string? Country { get; set; }

    public AddressInput ToInput() => new()
    {
        Line1 = FirstNonEmpty(Line1, Street, AddressLine1, Line1Snake),
        Line2 = FirstNonEmpty(Line2, AddressLine2, Line2Snake),
        City = City,
        Region = FirstNonEmpty(Region, State),
        PostalCode = FirstNonEmpty(PostalCode, ZipCode, Postal, PostalCodeSnake),
        Country = Country
    };

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

public class OrderItemRequest
{
    public int? CatalogItemId { get; set; }
    public int? ItemId { get; set; }
    public int? ProductId { get; set; }
    public int? Id { get; set; }
    public int? Quantity { get; set; }
    public int? Units { get; set; }
    public int? Qty { get; set; }
    public int? Count { get; set; }

    public int ResolveCatalogItemId() => CatalogItemId ?? ItemId ?? ProductId ?? Id ?? 0;

    public int ResolveQuantity() => Quantity ?? Units ?? Qty ?? Count ?? 1;
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequest>? Items { get; set; }
    public List<OrderItemRequest>? OrderItems { get; set; }
    public List<OrderItemRequest>? Lines { get; set; }
    public List<OrderItemRequest>? BasketItems { get; set; }
    public List<int>? CatalogItemIds { get; set; }
    public List<int>? Quantities { get; set; }
    public AddressRequest? ShippingAddress { get; set; }
    public AddressRequest? Address { get; set; }

    public IReadOnlyList<OrderLineInput> ResolveLines()
    {
        var source = Items ?? OrderItems ?? Lines ?? BasketItems;
        if (source is { Count: > 0 })
        {
            return source.Select(item => new OrderLineInput
            {
                CatalogItemId = item.ResolveCatalogItemId(),
                Quantity = item.ResolveQuantity()
            }).ToList();
        }

        if (CatalogItemIds is { Count: > 0 })
        {
            return CatalogItemIds.Select((id, index) => new OrderLineInput
            {
                CatalogItemId = id,
                Quantity = Quantities is not null && index < Quantities.Count ? Quantities[index] : 1
            }).ToList();
        }

        return Array.Empty<OrderLineInput>();
    }

    public AddressInput? ResolveAddress() => (ShippingAddress ?? Address)?.ToInput();
}

public class CardRequest
{
    public string? Number { get; set; }
    public string? CardNumber { get; set; }
    [JsonPropertyName("card_number")] public string? CardNumberSnake { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpMonth { get; set; }
    [JsonPropertyName("expiration_month")] public int? ExpirationMonthSnake { get; set; }
    [JsonPropertyName("expiry_month")] public int? ExpiryMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public int? ExpYear { get; set; }
    [JsonPropertyName("expiration_year")] public int? ExpirationYearSnake { get; set; }
    [JsonPropertyName("expiry_year")] public int? ExpiryYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvv { get; set; }
    public string? Cvc { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCodeSnake { get; set; }
    public string? Name { get; set; }
    public string? CardholderName { get; set; }
    public AddressRequest? Address { get; set; }
    public AddressRequest? BillingAddress { get; set; }

    public bool HasCardData =>
        !string.IsNullOrWhiteSpace(Number)
        || !string.IsNullOrWhiteSpace(CardNumber)
        || !string.IsNullOrWhiteSpace(CardNumberSnake);

    public CardDetailsInput ToInput() => new()
    {
        Number = FirstNonEmpty(Number, CardNumber, CardNumberSnake),
        ExpirationMonth = ExpirationMonth ?? ExpMonth ?? ExpirationMonthSnake ?? ExpiryMonth,
        ExpirationYear = ExpirationYear ?? ExpYear ?? ExpirationYearSnake ?? ExpiryYear,
        SecurityCode = FirstNonEmpty(SecurityCode, Cvv, Cvc, SecurityCodeSnake),
        Name = FirstNonEmpty(Name, CardholderName),
        Address = (Address ?? BillingAddress)?.ToInput()
    };

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

public class PayOrderRequest : BaseRequest
{
    public CardRequest? Card { get; set; }
    public JsonElement? PaymentMethodId { get; set; }
    public JsonElement? SavedPaymentMethodId { get; set; }
    public JsonElement? SavedCardId { get; set; }
    public JsonElement? PaymentMethod { get; set; }

    public string? Number { get; set; }
    public string? CardNumber { get; set; }
    [JsonPropertyName("card_number")] public string? CardNumberSnake { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpMonth { get; set; }
    [JsonPropertyName("expiration_month")] public int? ExpirationMonthSnake { get; set; }
    [JsonPropertyName("expiry_month")] public int? ExpiryMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public int? ExpYear { get; set; }
    [JsonPropertyName("expiration_year")] public int? ExpirationYearSnake { get; set; }
    [JsonPropertyName("expiry_year")] public int? ExpiryYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvv { get; set; }
    public string? Cvc { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCodeSnake { get; set; }
    public string? Name { get; set; }
    public string? CardholderName { get; set; }
    public AddressRequest? Address { get; set; }
    public AddressRequest? BillingAddress { get; set; }

    public CardDetailsInput? ResolveCard()
    {
        if (Card is not null && Card.HasCardData)
        {
            return Card.ToInput();
        }

        if (string.IsNullOrWhiteSpace(Number)
            && string.IsNullOrWhiteSpace(CardNumber)
            && string.IsNullOrWhiteSpace(CardNumberSnake))
        {
            return null;
        }

        return new CardDetailsInput
        {
            Number = Number ?? CardNumber ?? CardNumberSnake,
            ExpirationMonth = ExpirationMonth ?? ExpMonth ?? ExpirationMonthSnake ?? ExpiryMonth,
            ExpirationYear = ExpirationYear ?? ExpYear ?? ExpirationYearSnake ?? ExpiryYear,
            SecurityCode = SecurityCode ?? Cvv ?? Cvc ?? SecurityCodeSnake,
            Name = Name ?? CardholderName,
            Address = (Address ?? BillingAddress)?.ToInput()
        };
    }

    public string? ResolvePaymentMethodId() =>
        ElementToString(PaymentMethodId)
        ?? ElementToString(SavedPaymentMethodId)
        ?? ElementToString(SavedCardId)
        ?? ElementToString(PaymentMethod);

    private static string? ElementToString(JsonElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return element.Value.ValueKind switch
        {
            JsonValueKind.String => element.Value.GetString(),
            JsonValueKind.Number => element.Value.GetRawText(),
            _ => null
        };
    }
}

public class SavePaymentMethodRequest : BaseRequest
{
    public CardRequest? Card { get; set; }
    public string? Number { get; set; }
    public string? CardNumber { get; set; }
    [JsonPropertyName("card_number")] public string? CardNumberSnake { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpMonth { get; set; }
    [JsonPropertyName("expiration_month")] public int? ExpirationMonthSnake { get; set; }
    [JsonPropertyName("expiry_month")] public int? ExpiryMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public int? ExpYear { get; set; }
    [JsonPropertyName("expiration_year")] public int? ExpirationYearSnake { get; set; }
    [JsonPropertyName("expiry_year")] public int? ExpiryYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvv { get; set; }
    public string? Cvc { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCodeSnake { get; set; }
    public string? Name { get; set; }
    public string? CardholderName { get; set; }
    public AddressRequest? Address { get; set; }
    public AddressRequest? BillingAddress { get; set; }

    public CardDetailsInput ResolveCard()
    {
        if (Card is not null && Card.HasCardData)
        {
            return Card.ToInput();
        }

        return new CardDetailsInput
        {
            Number = Number ?? CardNumber ?? CardNumberSnake,
            ExpirationMonth = ExpirationMonth ?? ExpMonth ?? ExpirationMonthSnake ?? ExpiryMonth,
            ExpirationYear = ExpirationYear ?? ExpYear ?? ExpirationYearSnake ?? ExpiryYear,
            SecurityCode = SecurityCode ?? Cvv ?? Cvc ?? SecurityCodeSnake,
            Name = Name ?? CardholderName,
            Address = (Address ?? BillingAddress)?.ToInput()
        };
    }
}

public class RefundOrderRequest : BaseRequest
{
    public decimal? Amount { get; set; }
    public decimal? RefundAmount { get; set; }
    [JsonPropertyName("refund_amount")] public decimal? RefundAmountSnake { get; set; }
    public long? AmountInCents { get; set; }
    public long? AmountCents { get; set; }
    [JsonPropertyName("amount_in_cents")] public long? AmountInCentsSnake { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? IdempotencyId { get; set; }
    [JsonPropertyName("idempotency_id")] public string? IdempotencyIdSnake { get; set; }
    public string? RequestId { get; set; }

    public decimal? ResolveAmount() => Amount ?? RefundAmount ?? RefundAmountSnake;
    public long? ResolveAmountInCents() => AmountInCents ?? AmountCents ?? AmountInCentsSnake;
    public string? ResolveIdempotencyKey() => IdempotencyKey ?? IdempotencyId ?? IdempotencyIdSnake ?? RequestId;
}
