namespace Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;

// --- Vault v3: payment tokens (save a card) ---

internal sealed class CreatePaymentTokenRequestDto
{
    public VaultPaymentSourceDto? PaymentSource { get; set; }
    public VaultCustomerDto? Customer { get; set; }
}

internal sealed class VaultPaymentSourceDto
{
    public VaultCardDto? Card { get; set; }
}

internal sealed class VaultCardDto
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }         // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardBillingAddressDto? BillingAddress { get; set; }
}

internal sealed class VaultCustomerDto
{
    public string? Id { get; set; }
}

internal sealed class PaymentTokenResponseDto
{
    public string? Id { get; set; }
    public VaultCustomerDto? Customer { get; set; }
    public VaultPaymentSourceResponseDto? PaymentSource { get; set; }
}

internal sealed class VaultPaymentSourceResponseDto
{
    public CardResponseDto? Card { get; set; }
}

internal sealed class CardResponseDto
{
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? Name { get; set; }
}
