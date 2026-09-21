using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The action to take on the subscription if the initial payment for the setup fails.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SetupFeeFailureAction>))]
public sealed record SetupFeeFailureAction : StringEnum<SetupFeeFailureAction>
{
    private SetupFeeFailureAction(string value) : base(value)
    {
    }

    /// <summary>
    /// Continues the subscription if the initial payment for the setup fails.
    /// </summary>
    public static readonly SetupFeeFailureAction Continue = new("CONTINUE");

    /// <summary>
    /// Cancels the subscription if the initial payment for the setup fails.
    /// </summary>
    public static readonly SetupFeeFailureAction Cancel = new("CANCEL");

    public static SetupFeeFailureAction FromValue(string value) => FromValueCore(value);
}
