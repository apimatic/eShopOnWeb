using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record SubscriptionGroupBankAccount
{
    /// <summary>
    /// (Required when creating a subscription with ACH or GoCardless) The name of the bank where the customer’s account resides
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bank_name")]
    public string? BankName { get; init; }

    /// <summary>
    /// (Required when creating a subscription with ACH. Required when creating a subscription with GoCardless and bank_iban is blank) The customerʼs bank account number
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bank_account_number")]
    public string? BankAccountNumber { get; init; }

    /// <summary>
    /// (Required when creating a subscription with ACH. Optional when creating a subscription with GoCardless.) The routing number of the bank. It becomes bank_code while passing via GoCardless API.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bank_routing_number")]
    public string? BankRoutingNumber { get; init; }

    /// <summary>
    /// (Optional when creating a subscription with GoCardless). International Bank Account Number. Alternatively, local bank details can be provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bank_iban")]
    public string? BankIban { get; init; }

    /// <summary>
    /// (Optional when creating a subscription with GoCardless) Branch code. Alternatively, an IBAN can be provided.
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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_type")]
    public PaymentType? PaymentType { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_address")]
    public string? BillingAddress { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_city")]
    public string? BillingCity { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_state")]
    public string? BillingState { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_zip")]
    public string? BillingZip { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_country")]
    public string? BillingCountry { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("chargify_token")]
    public string? ChargifyToken { get; init; }

    /// <summary>
    /// The vault that stores the payment profile with the provided vault_token. Use <c>bogus</c> for testing.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("current_vault")]
    public BankAccountVault? CurrentVault { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("gateway_handle")]
    public string? GatewayHandle { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
