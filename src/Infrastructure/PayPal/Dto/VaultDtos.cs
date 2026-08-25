using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Dto;

// vault_payment_tokens_v3: POST /v3/vault/payment-tokens, DELETE /v3/vault/payment-tokens/{id}
// "customer" is intentionally omitted from requests: eShop tracks card ownership itself (Buyer.PaymentMethods),
// so there is no need to rely on PayPal's customer.id semantics, which the spec/docs describe inconsistently.

public class PaymentTokenRequestDto
{
    [JsonPropertyName("payment_source")] public PaymentTokenSourceDto PaymentSource { get; set; } = null!;
}

public class PaymentTokenSourceDto
{
    [JsonPropertyName("card")] public CardRequestDto? Card { get; set; }
}

public class PaymentTokenResponseDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = null!;
    [JsonPropertyName("payment_source")] public PaymentTokenResponseSourceDto? PaymentSource { get; set; }
}

public class PaymentTokenResponseSourceDto
{
    [JsonPropertyName("card")] public CardResponseDto? Card { get; set; }
}
