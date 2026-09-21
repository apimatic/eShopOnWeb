using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The reason why the refund has the <c>PENDING</c> or <c>FAILED</c> status.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RefundIncompleteReason>))]
public sealed record RefundIncompleteReason : StringEnum<RefundIncompleteReason>
{
    private RefundIncompleteReason(string value) : base(value)
    {
    }

    /// <summary>
    /// The customer's account is funded through an eCheck, which has not yet cleared.
    /// </summary>
    public static readonly RefundIncompleteReason Echeck = new("ECHECK");

    public static RefundIncompleteReason FromValue(string value) => FromValueCore(value);
}
