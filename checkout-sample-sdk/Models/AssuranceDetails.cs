using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Information about cardholder possession validation and cardholder identification and verifications (ID&amp;V).
/// </summary>
public record AssuranceDetails
{
    /// <summary>
    /// If true, indicates that Cardholder possession validation has been performed on returned payment credential.
    /// </summary>
    [JsonPropertyName("account_verified")]
    public bool? AccountVerified { get; init; } = false;

    /// <summary>
    /// If true, indicates that identification and verifications (ID&amp;V) was performed on the returned payment credential.If false, the same risk-based authentication can be performed as you would for card transactions. This risk-based authentication can include, but not limited to, step-up with 3D Secure protocol if applicable.
    /// </summary>
    [JsonPropertyName("card_holder_authenticated")]
    public bool? CardHolderAuthenticated { get; init; } = false;
}
