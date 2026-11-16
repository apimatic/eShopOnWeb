using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public sealed class CreateOrderRequest
{
    public List<CreateOrderItemRequest>? Items { get; set; }
    public ShippingAddressRequest? ShippingAddress { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }

    public CreateOrderCommand ToCommand()
    {
        if (Items is null || Items.Count == 0)
            throw new PaymentOperationException(400, "empty_order",
                "The request must include at least one catalog item.");

        return new CreateOrderCommand(
            Items.Select(i => new CreateOrderItem(i.CatalogItemId, i.Quantity)).ToList(),
            ShippingAddress is null
                ? null
                : new ShippingAddressDetails(ShippingAddress.Street ?? string.Empty,
                    ShippingAddress.City ?? string.Empty, ShippingAddress.State ?? string.Empty,
                    ShippingAddress.Country ?? string.Empty, ShippingAddress.ZipCode ?? string.Empty));
    }
}

public sealed class CardAddressRequest
{
    [JsonPropertyName("line1")] public string? Line1 { get; set; }
    [JsonPropertyName("addressLine1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("region")] public string? Region { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("postalCode")] public string? PostalCode { get; set; }
    [JsonPropertyName("zipCode")] public string? ZipCode { get; set; }
    [JsonPropertyName("zip")] public string? Zip { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }

    public string? ResolveLine1() => Line1 ?? AddressLine1;
    public string? ResolveRegion() => Region ?? State;
    public string? ResolvePostalCode() => PostalCode ?? ZipCode ?? Zip;
}

public class CardRequest
{
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("cardNumber")] public string? CardNumber { get; set; }
    [JsonPropertyName("expirationMonth")] public int? ExpirationMonth { get; set; }
    [JsonPropertyName("expiryMonth")] public int? ExpiryMonth { get; set; }
    [JsonPropertyName("expirationYear")] public int? ExpirationYear { get; set; }
    [JsonPropertyName("expiryYear")] public int? ExpiryYear { get; set; }
    [JsonPropertyName("securityCode")] public string? SecurityCode { get; set; }
    [JsonPropertyName("cvc")] public string? Cvc { get; set; }
    [JsonPropertyName("cvv")] public string? Cvv { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("cardholderName")] public string? CardholderName { get; set; }
    [JsonPropertyName("addressLine1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("region")] public string? Region { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("postalCode")] public string? PostalCode { get; set; }
    [JsonPropertyName("zipCode")] public string? ZipCode { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }
    [JsonPropertyName("address")] public CardAddressRequest? Address { get; set; }

    [JsonIgnore]
    public bool HasCardFields =>
        !string.IsNullOrWhiteSpace(Number) || !string.IsNullOrWhiteSpace(CardNumber)
        || ExpirationMonth.HasValue || ExpiryMonth.HasValue
        || ExpirationYear.HasValue || ExpiryYear.HasValue;

    public CardDetails ToCardDetails()
    {
        var number = Number ?? CardNumber;
        var month = ExpirationMonth ?? ExpiryMonth;
        var year = ExpirationYear ?? ExpiryYear;
        var securityCode = SecurityCode ?? Cvc ?? Cvv;
        var name = Name ?? CardholderName;
        var line1 = Address?.ResolveLine1() ?? AddressLine1;
        var city = Address?.City ?? City;
        var region = Address?.ResolveRegion() ?? Region ?? State;
        var postalCode = Address?.ResolvePostalCode() ?? PostalCode ?? ZipCode;
        var country = Address?.Country ?? Country;

        if (string.IsNullOrWhiteSpace(number) || !month.HasValue || !year.HasValue
            || string.IsNullOrWhiteSpace(securityCode) || string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentOperationException(400, "invalid_card",
                "Card details require number, expirationMonth, expirationYear, securityCode and name.");
        }

        return new CardDetails(number!, month.Value, year.Value, securityCode!, name!,
            line1, city, region, postalCode, country);
    }
}

public sealed class PayOrderRequest : CardRequest
{
    public int? PaymentMethodId { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public CardRequest? Card { get; set; }
    public CardRequest? CardDetails { get; set; }
    public string? IdempotencyKey { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }

    public PayOrderCommand ToCommand()
    {
        var paymentMethodId = PaymentMethodId ?? SavedPaymentMethodId;
        var card = Card ?? CardDetails ?? (HasCardFields ? this : null);
        return new PayOrderCommand(paymentMethodId, card?.ToCardDetails(), IdempotencyKey);
    }
}

public sealed class SavePaymentMethodRequest : CardRequest
{
    public CardRequest? Card { get; set; }
    public CardRequest? CardDetails { get; set; }
    public string? Alias { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }

    public SaveCardCommand ToCommand()
    {
        var card = Card ?? CardDetails ?? (HasCardFields ? this : null);
        if (card is null)
            throw new PaymentOperationException(400, "invalid_card", "Card details are required.");

        return new SaveCardCommand(card.ToCardDetails(), Alias);
    }
}

public sealed class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    public long? AmountInCents { get; set; }
    public string? IdempotencyKey { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }

    [JsonIgnore]
    public bool IsAdministrator { get; set; }

    public RefundCommand ToCommand(string? headerIdempotencyKey)
    {
        long? cents = AmountInCents;
        if (!cents.HasValue && Amount.HasValue)
            cents = (long)System.Math.Round(Amount.Value * 100m, 0,
                System.MidpointRounding.AwayFromZero);

        var key = string.IsNullOrWhiteSpace(IdempotencyKey) ? headerIdempotencyKey : IdempotencyKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new PaymentOperationException(400, "idempotency_key_required",
                "A caller-supplied idempotencyKey is required for refunds.");

        return new RefundCommand(cents, key!);
    }
}

public sealed class FulfilOrderRequest
{
    [JsonIgnore] public int OrderId { get; set; }
}

public sealed class CancelOrderRequest
{
    [JsonIgnore] public int OrderId { get; set; }
}

public sealed class GetMyOrdersRequest
{
    [JsonIgnore] public string? BuyerId { get; set; }
}

public sealed class ReconciliationRequest
{
    public System.DateTimeOffset? From { get; set; }
    public System.DateTimeOffset? To { get; set; }
}

public sealed class DeletePaymentMethodRequest
{
    [JsonIgnore] public int PaymentMethodId { get; set; }
    [JsonIgnore] public string? BuyerId { get; set; }
}

public sealed class ListPaymentMethodsRequest
{
    [JsonIgnore] public string? BuyerId { get; set; }
}

internal static class PaymentEndpointUser
{
    public static string GetBuyerId(HttpContext context)
    {
        var buyerId = context.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new PaymentOperationException(401, "unauthenticated",
                "The request must be authenticated with a shopper token.");
        return buyerId;
    }

    public static bool IsAdministrator(HttpContext context) =>
        context.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
