using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;

// --- Orders v2: create order (intent=AUTHORIZE, direct card / vaulted card) ---

internal sealed class CreateOrderRequestDto
{
    public string Intent { get; set; } = "AUTHORIZE";
    public List<PurchaseUnitRequestDto> PurchaseUnits { get; set; } = new();
    public OrderPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PurchaseUnitRequestDto
{
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public MoneyDto? Amount { get; set; }
    public string? SoftDescriptor { get; set; }
}

internal sealed class OrderPaymentSourceDto
{
    public CardRequestDto? Card { get; set; }
}

internal sealed class CardRequestDto
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }         // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardBillingAddressDto? BillingAddress { get; set; }
    public string? VaultId { get; set; }        // pay with a saved card
}

internal sealed class CardBillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }     // state / province
    public string? AdminArea2 { get; set; }     // city
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }    // required by PayPal
}

// --- Orders v2: order response ---

internal sealed class OrderResponseDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public List<PurchaseUnitResponseDto>? PurchaseUnits { get; set; }
    public List<LinkDto>? Links { get; set; }
}

internal sealed class PurchaseUnitResponseDto
{
    public PaymentCollectionDto? Payments { get; set; }
}

internal sealed class PaymentCollectionDto
{
    public List<AuthorizationDto>? Authorizations { get; set; }
    public List<CaptureDto>? Captures { get; set; }
    public List<RefundDto>? Refunds { get; set; }
}
