using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record CreatePaymentProfile
{
    /// <summary>
    /// Token received after sending billing information using Maxio.js (formerly Chargify.js).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("chargify_token")]
    public string? ChargifyToken { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public int? Id { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_type")]
    public PaymentType? PaymentType { get; init; }

    /// <summary>
    /// First name on card or bank account. If omitted, the first_name from customer attributes will be used.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("first_name")]
    public string? FirstName { get; init; }

    /// <summary>
    /// Last name on card or bank account. If omitted, the last_name from customer attributes will be used.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("masked_card_number")]
    public string? MaskedCardNumber { get; init; }

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
    public ExpirationMonth1? ExpirationMonth { get; init; }

    /// <summary>
    /// (Optional when performing an Import via vault_token, required otherwise) The 4-digit credit card expiration year, as an integer or string, e.g., 2012
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiration_year")]
    public ExpirationYear1? ExpirationYear { get; init; }

    /// <summary>
    /// The credit card or bank account billing street address (e.g., 123 Main St.). This value is merely passed through to the payment gateway.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_address")]
    public string? BillingAddress { get; init; }

    /// <summary>
    /// Second line of the customer’s billing address e.g., Apt. 100
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_address_2")]
    public string? BillingAddress2 { get; init; }

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
    /// “The credit card or bank account billing address country, required in <see href="https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2">ISO_3166-1 alpha-2</see> format (e.g., “US”). This value is merely passed through to the payment gateway. Some gateways require country codes in a specific format. Check your gateway’s documentation. If creating an ACH subscription, only US is supported at this time.”
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_country")]
    public string? BillingCountry { get; init; }

    /// <summary>
    /// The credit card or bank account billing address zip code (e.g., 12345). This value is merely passed through to the payment gateway.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_zip")]
    public string? BillingZip { get; init; }

    /// <summary>
    /// The vault that stores the payment profile with the provided <c>vault_token</c>. Use <c>bogus</c> for testing.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("current_vault")]
    public AllVaults? CurrentVault { get; init; }

    /// <summary>
    /// The “token” provided by your vault storage for an already stored payment profile
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vault_token")]
    public string? VaultToken { get; init; }

    /// <summary>
    /// (only for Authorize.Net CIM storage or Square) The customerProfileId for the owner of the customerPaymentProfileId provided as the vault_token
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer_vault_token")]
    public string? CustomerVaultToken { get; init; }

    /// <summary>
    /// (Required when creating a new payment profile) The Chargify customer id.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; init; }

    /// <summary>
    /// used by merchants that implemented BraintreeBlue javaScript libraries on their own. We recommend using Maxio.js (formerly Chargify.js) instead.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("paypal_email")]
    public string? PaypalEmail { get; init; }

    /// <summary>
    /// used by merchants that implemented BraintreeBlue javaScript libraries on their own. We recommend using Maxio.js (formerly Chargify.js) instead.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_method_nonce")]
    public string? PaymentMethodNonce { get; init; }

    /// <summary>
    /// This attribute is only available if MultiGateway feature is enabled for your Site. This feature is in the Private Beta currently. gateway_handle is used to directly select a gateway where a payment profile will be stored in. Every connected gateway must have a unique gateway handle specified. Read <see href="https://chargify.zendesk.com/hc/en-us/articles/4407761759643#connecting-with-multiple-gateways">Multigateway description</see> to learn more about new concepts that MultiGateway introduces and the default behavior when this attribute is not passed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("gateway_handle")]
    public string? GatewayHandle { get; init; }

    /// <summary>
    /// The 3- or 4-digit Card Verification Value. This value is merely passed through to the payment gateway.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cvv")]
    public string? Cvv { get; init; }

    /// <summary>
    /// (Required when creating with ACH or GoCardless, optional with Stripe Direct Debit). The name of the bank where the customerʼs account resides
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bank_name")]
    public string? BankName { get; init; }

    /// <summary>
    /// (Optional when creating with GoCardless, required with Stripe Direct Debit). International Bank Account Number. Alternatively, local bank details can be provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bank_iban")]
    public string? BankIban { get; init; }

    /// <summary>
    /// (Required when creating with ACH. Optional when creating a subscription with GoCardless). The routing number of the bank. It becomes bank_code while passing via GoCardless API.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bank_routing_number")]
    public string? BankRoutingNumber { get; init; }

    /// <summary>
    /// (Required when creating with ACH, GoCardless, Stripe BECS or BACS Direct Debit, and bank_iban is blank) The customerʼs bank account number
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bank_account_number")]
    public string? BankAccountNumber { get; init; }

    /// <summary>
    /// (Optional when creating with GoCardless, required with Stripe BECS or BACS Direct Debit) Branch/Sort code. Alternatively, an IBAN can be provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bank_branch_code")]
    public string? BankBranchCode { get; init; }

    /// <summary>
    /// Defaults to checking
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bank_account_type")]
    public BankAccountType? BankAccountType { get; init; }

    /// <summary>
    /// Defaults to personal
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bank_account_holder_type")]
    public BankAccountHolderType? BankAccountHolderType { get; init; }

    /// <summary>
    /// (Optional) Used for creating subscription with payment profile imported using vault_token, for proper display in Advanced Billing UI
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("last_four")]
    public string? LastFour { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
