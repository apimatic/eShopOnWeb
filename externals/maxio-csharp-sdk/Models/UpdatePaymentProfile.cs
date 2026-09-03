using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record UpdatePaymentProfile
{
    /// <summary>
    /// The first name of the card holder.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("first_name")]
    public string? FirstName { get; init; }

    /// <summary>
    /// The last name of the card holder.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }

    /// <summary>
    /// The full credit card number
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("full_number")]
    public string? FullNumber { get; init; }

    /// <summary>
    /// The type of card used.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("card_type")]
    public CardType? CardType { get; init; }

    /// <summary>
    /// (Optional when performing an Import via vault_token, required otherwise) The 1- or 2-digit credit card expiration month, as an integer or string, e.g., 5
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiration_month")]
    public string? ExpirationMonth { get; init; }

    /// <summary>
    /// (Optional when performing an Import via vault_token, required otherwise) The 4-digit credit card expiration year, as an integer or string, e.g., 2012
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiration_year")]
    public string? ExpirationYear { get; init; }

    /// <summary>
    /// The vault that stores the payment profile with the provided <c>vault_token</c>. Use <c>bogus</c> for testing.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("current_vault")]
    public AllVaults? CurrentVault { get; init; }

    /// <summary>
    /// The credit card or bank account billing street address (e.g., 123 Main St.). This value is merely passed through to the payment gateway.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_address")]
    public string? BillingAddress { get; init; }

    /// <summary>
    /// The credit card or bank account billing address city (e.g., “Boston”). This value is merely passed through to the payment gateway.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_city")]
    public string? BillingCity { get; init; }

    /// <summary>
    /// The credit card or bank account billing address state (e.g., MA). This value is merely passed through to the payment gateway. This must conform to the <see href="https://en.wikipedia.org/wiki/ISO_3166-1#Current_codes">ISO_3166-1</see> in order to be valid for tax locale purposes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_state")]
    public string? BillingState { get; init; }

    /// <summary>
    /// The credit card or bank account billing address zip code (e.g., 12345). This value is merely passed through to the payment gateway.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_zip")]
    public string? BillingZip { get; init; }

    /// <summary>
    /// The credit card or bank account billing address country, required in <see href="https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2">ISO_3166-1 alpha-2</see> format (e.g., “US”). This value is merely passed through to the payment gateway. Some gateways require country codes in a specific format. Check your gateway’s documentation. If creating an ACH subscription, only US is supported at this time.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_country")]
    public string? BillingCountry { get; init; }

    /// <summary>
    /// Second line of the customer’s billing address, e.g., Apt. 100
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_address_2")]
    public string? BillingAddress2 { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
