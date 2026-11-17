using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payment;

public class PaymentAddress
{
    public string Line1 { get; set; } = "123 Main St";
    public string? Line2 { get; set; }
    public string City { get; set; } = "San Francisco";
    public string Region { get; set; } = "CA";
    public string PostalCode { get; set; } = "94105";
    public string Country { get; set; } = "USA";
}

public class CardPaymentDetails
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public PaymentAddress? Address { get; set; }
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderCommand
{
    public List<CreateOrderItem> Items { get; set; } = new();
    public PaymentAddress? ShippingAddress { get; set; }
}

public class PayOrderCommand
{
    public CardPaymentDetails? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class RefundCommand
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class SaveCardCommand
{
    public CardPaymentDetails Card { get; set; } = new();
    public string? Alias { get; set; }
}
