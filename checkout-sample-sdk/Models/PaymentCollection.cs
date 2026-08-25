using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The collection of payments, or transactions, for a purchase unit in an order. For example, authorized payments, captured payments, and refunds.
/// </summary>
public record PaymentCollection
{
    /// <summary>
    /// An array of authorized payments for a purchase unit. A purchase unit can have zero or more authorized payments.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("authorizations")]
    public IReadOnlyList<AuthorizationWithAdditionalData>? Authorizations { get; init; }

    /// <summary>
    /// An array of captured payments for a purchase unit. A purchase unit can have zero or more captured payments.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("captures")]
    public IReadOnlyList<OrdersCapture>? Captures { get; init; }

    /// <summary>
    /// An array of refunds for a purchase unit. A purchase unit can have zero or more refunds.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("refunds")]
    public IReadOnlyList<Refund>? Refunds { get; init; }
}
