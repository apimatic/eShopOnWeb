using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// Provides additional details to process a payment using a <c>card</c> that has been stored or is intended to be stored (also referred to as stored_credential or card-on-file). Parameter compatibility: <c>payment_type=ONE_TIME</c> is compatible only with <c>payment_initiator=CUSTOMER</c>. <c>usage=FIRST</c> is compatible only with <c>payment_initiator=CUSTOMER</c>. <c>previous_transaction_reference</c> or <c>previous_network_transaction_reference</c> is compatible only with <c>payment_initiator=MERCHANT</c>. Only one of the parameters - <c>previous_transaction_reference</c> and <c>previous_network_transaction_reference</c> - can be present in the request.
/// </summary>
public record CardStoredCredential
{
    /// <summary>
    /// The person or party who initiated or triggered the payment.
    /// </summary>
    [JsonPropertyName("payment_initiator")]
    public required PaymentInitiator PaymentInitiator { get; init; }

    /// <summary>
    /// Indicates the type of the stored payment_source payment.
    /// </summary>
    [JsonPropertyName("payment_type")]
    public required StoredPaymentSourcePaymentType PaymentType { get; init; }

    /// <summary>
    /// Indicates if this is a <c>first</c> or <c>subsequent</c> payment using a stored payment source (also referred to as stored credential or card on file).
    /// </summary>
    [JsonPropertyName("usage")]
    public StoredPaymentSourceUsageType? Usage { get; init; } = StoredPaymentSourceUsageType.Derived;

    /// <summary>
    /// Reference values used by the card network to identify a transaction.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("previous_network_transaction_reference")]
    public NetworkTransaction? PreviousNetworkTransactionReference { get; init; }
}
