using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// Any additional payment instructions to be consider during payment processing. This processing instruction is applicable for Capturing an order or Authorizing an Order.
/// </summary>
public record CapturePaymentInstruction
{
    /// <summary>
    /// An array of platform or partner fees, commissions, or brokerage fees that associated with the captured payment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("platform_fees")]
    [MinLength(0)]
    [MaxLength(1)]
    public IReadOnlyList<PlatformFee>? PlatformFees { get; init; }

    /// <summary>
    /// The funds that are held on behalf of the merchant.
    /// </summary>
    [JsonPropertyName("disbursement_mode")]
    public DisbursementMode? DisbursementMode { get; init; } = DisbursementMode.Instant;

    /// <summary>
    /// FX identifier generated returned by PayPal to be used for payment processing in order to honor FX rate (for eligible integrations) to be used when amount is settled/received into the payee account.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payee_receivable_fx_rate_id")]
    [StringLength(4000, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? PayeeReceivableFxRateId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
