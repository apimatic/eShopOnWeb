using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int? Id { get; set; }
    public int Quantity { get; set; } = 1;
    public int? Units { get; set; }

    public OrderLineInput ToInput()
    {
        return new OrderLineInput
        {
            CatalogItemId = CatalogItemId != 0 ? CatalogItemId : Id ?? 0,
            Quantity = Units is > 0 ? Units.Value : Quantity
        };
    }
}

public class AddressRequest
{
    public string? Street { get; set; }
    public string? Line1 { get; set; }
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
    public string? PostalCode { get; set; }

    public ShippingAddressInput ToInput()
    {
        return new ShippingAddressInput
        {
            Street = FirstNonEmpty(Street, Line1, AddressLine1) ?? "N/A",
            City = FirstNonEmpty(City) ?? "N/A",
            State = FirstNonEmpty(State, Region) ?? "NA",
            Country = FirstNonEmpty(Country) ?? "USA",
            ZipCode = FirstNonEmpty(ZipCode, PostalCode) ?? "00000"
        };
    }

    public FinixAddress? ToFinixAddress()
    {
        var street = FirstNonEmpty(Street, Line1, AddressLine1);
        var city = FirstNonEmpty(City);
        var region = FirstNonEmpty(State, Region);
        var postalCode = FirstNonEmpty(ZipCode, PostalCode);
        var country = FirstNonEmpty(Country);

        if (street is null && city is null && region is null && postalCode is null && country is null)
        {
            return null;
        }

        return new FinixAddress
        {
            Line1 = street ?? "N/A",
            City = city ?? "N/A",
            Region = region ?? "NA",
            PostalCode = postalCode ?? "00000",
            Country = country ?? "USA"
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}

public class CreateOrderRequest
{
    public List<OrderLineRequest>? Items { get; set; }
    public List<OrderLineRequest>? OrderItems { get; set; }
    public List<OrderLineRequest>? Lines { get; set; }
    public List<OrderLineRequest>? CatalogItems { get; set; }
    public int? CatalogItemId { get; set; }
    public int? Quantity { get; set; }
    public int? Units { get; set; }
    public AddressRequest? ShipToAddress { get; set; }
    public AddressRequest? ShippingAddress { get; set; }
    public AddressRequest? Address { get; set; }

    public IReadOnlyList<OrderLineInput> ResolveItems()
    {
        var lines = FirstNonEmptyList(Items, OrderItems, Lines, CatalogItems);
        if (lines is not null)
        {
            return lines.Select(line => line.ToInput()).ToList();
        }

        if (CatalogItemId is > 0)
        {
            return new List<OrderLineInput>
            {
                new()
                {
                    CatalogItemId = CatalogItemId.Value,
                    Quantity = Units is > 0 ? Units.Value : Quantity ?? 1
                }
            };
        }

        return new List<OrderLineInput>();
    }

    public ShippingAddressInput ResolveAddress()
    {
        var address = ShipToAddress ?? ShippingAddress ?? Address;
        return address?.ToInput() ?? new ShippingAddressInput();
    }

    private static List<OrderLineRequest>? FirstNonEmptyList(params List<OrderLineRequest>?[] lists)
    {
        return lists.FirstOrDefault(list => list is { Count: > 0 });
    }
}

public class CardRequest
{
    public string? Number { get; set; }
    public string? CardNumber { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpYear { get; set; }
    public string? Exp { get; set; }
    public string? Expiration { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvc { get; set; }
    public string? Cvv { get; set; }
    public string? Name { get; set; }
    public AddressRequest? Address { get; set; }
    public AddressRequest? BillingAddress { get; set; }

    public CardDetails ToCardDetails()
    {
        var (month, year) = ResolveExpiry();
        return new CardDetails
        {
            Number = FirstNonEmpty(Number, CardNumber) ?? string.Empty,
            ExpirationMonth = month,
            ExpirationYear = year,
            SecurityCode = FirstNonEmpty(SecurityCode, Cvc, Cvv),
            Name = FirstNonEmpty(Name) ?? "eShop Shopper",
            Address = (Address ?? BillingAddress)?.ToFinixAddress()
        };
    }

    private (int Month, int Year) ResolveExpiry()
    {
        int? month = ExpirationMonth ?? ExpiryMonth ?? ExpMonth;
        int? year = ExpirationYear ?? ExpiryYear ?? ExpYear;

        var text = FirstNonEmpty(Exp, Expiration);
        if ((month is null || year is null) && !string.IsNullOrWhiteSpace(text))
        {
            var parts = text.Split(new[] { '/', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                if (int.TryParse(parts[0], out var parsedMonth))
                {
                    month ??= parsedMonth;
                }

                if (int.TryParse(parts[1], out var parsedYear))
                {
                    year ??= parsedYear < 100 ? 2000 + parsedYear : parsedYear;
                }
            }
        }

        return (month ?? 0, year ?? 0);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}

public class PayOrderRequest
{
    public int OrderId { get; set; }
    public int? PaymentMethodId { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public int? SavedCardId { get; set; }
    public CardRequest? Card { get; set; }
    public string? CardNumber { get; set; }
    public string? Number { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpYear { get; set; }
    public string? Exp { get; set; }
    public string? Expiration { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvc { get; set; }
    public string? Cvv { get; set; }
    public string? Name { get; set; }
    public AddressRequest? BillingAddress { get; set; }
    public AddressRequest? Address { get; set; }

    public PayOrderInput ToInput()
    {
        var card = Card;
        if (card is null && (!string.IsNullOrWhiteSpace(CardNumber) || !string.IsNullOrWhiteSpace(Number)))
        {
            card = new CardRequest
            {
                Number = CardNumber,
                CardNumber = Number,
                ExpirationMonth = ExpirationMonth,
                ExpirationYear = ExpirationYear,
                ExpiryMonth = ExpiryMonth,
                ExpiryYear = ExpiryYear,
                ExpMonth = ExpMonth,
                ExpYear = ExpYear,
                Exp = Exp,
                Expiration = Expiration,
                SecurityCode = SecurityCode,
                Cvc = Cvc,
                Cvv = Cvv,
                Name = Name,
                BillingAddress = BillingAddress,
                Address = Address
            };
        }

        return new PayOrderInput
        {
            PaymentMethodId = PaymentMethodId ?? SavedPaymentMethodId ?? SavedCardId,
            Card = card?.ToCardDetails()
        };
    }
}

public class RefundOrderRequest
{
    public int OrderId { get; set; }
    public long? AmountCents { get; set; }
    public decimal? Amount { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? IdempotencyId { get; set; }

    public RefundInput ToInput()
    {
        return new RefundInput
        {
            AmountCents = AmountCents,
            Amount = Amount,
            IdempotencyKey = string.IsNullOrWhiteSpace(IdempotencyKey) ? IdempotencyId : IdempotencyKey
        };
    }
}

public class SavePaymentMethodRequest
{
    public CardRequest? Card { get; set; }
    public string? CardNumber { get; set; }
    public string? Number { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpYear { get; set; }
    public string? Exp { get; set; }
    public string? Expiration { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvc { get; set; }
    public string? Cvv { get; set; }
    public string? Name { get; set; }
    public AddressRequest? BillingAddress { get; set; }
    public AddressRequest? Address { get; set; }

    public CardDetails ToCardDetails()
    {
        if (Card is not null)
        {
            return Card.ToCardDetails();
        }

        return new CardRequest
        {
            Number = CardNumber,
            CardNumber = Number,
            ExpirationMonth = ExpirationMonth,
            ExpirationYear = ExpirationYear,
            ExpiryMonth = ExpiryMonth,
            ExpiryYear = ExpiryYear,
            ExpMonth = ExpMonth,
            ExpYear = ExpYear,
            Exp = Exp,
            Expiration = Expiration,
            SecurityCode = SecurityCode,
            Cvc = Cvc,
            Cvv = Cvv,
            Name = Name,
            BillingAddress = BillingAddress,
            Address = Address
        }.ToCardDetails();
    }
}
