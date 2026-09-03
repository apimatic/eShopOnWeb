using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record GetOneTimeTokenPaymentProfile
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("first_name")]
    [MinLength(1)]
    public required string FirstName { get; init; }

    [JsonPropertyName("last_name")]
    [MinLength(1)]
    public required string LastName { get; init; }

    [JsonPropertyName("masked_card_number")]
    [MinLength(1)]
    public required string MaskedCardNumber { get; init; }

    /// <summary>
    /// The type of card used.
    /// </summary>
    [JsonPropertyName("card_type")]
    public required CardType CardType { get; init; }

    [JsonPropertyName("expiration_month")]
    public required double ExpirationMonth { get; init; }

    [JsonPropertyName("expiration_year")]
    public required double ExpirationYear { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer_id")]
    public string? CustomerId { get; init; }

    /// <summary>
    /// The vault that stores the payment profile with the provided <c>vault_token</c>. Use <c>bogus</c> for testing.
    /// </summary>
    [JsonPropertyName("current_vault")]
    public required CreditCardVault CurrentVault { get; init; }

    [JsonPropertyName("vault_token")]
    [MinLength(1)]
    public required string VaultToken { get; init; }

    [JsonPropertyName("billing_address")]
    [MinLength(1)]
    public required string BillingAddress { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_address_2")]
    public string? BillingAddress2 { get; init; }

    [JsonPropertyName("billing_city")]
    [MinLength(1)]
    public required string BillingCity { get; init; }

    [JsonPropertyName("billing_country")]
    [MinLength(1)]
    public required string BillingCountry { get; init; }

    [JsonPropertyName("billing_state")]
    [MinLength(1)]
    public required string BillingState { get; init; }

    [JsonPropertyName("billing_zip")]
    [MinLength(1)]
    public required string BillingZip { get; init; }

    [JsonPropertyName("payment_type")]
    [MinLength(1)]
    public required string PaymentType { get; init; }

    [JsonPropertyName("disabled")]
    public required bool Disabled { get; init; }

    [JsonPropertyName("site_gateway_setting_id")]
    public required int SiteGatewaySettingId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer_vault_token")]
    public string? CustomerVaultToken { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("gateway_handle")]
    public string? GatewayHandle { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
