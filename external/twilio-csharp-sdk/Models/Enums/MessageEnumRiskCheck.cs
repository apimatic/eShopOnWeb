using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Include this parameter with a value of <c>disable</c> to skip any kind of risk check on the respective message request., Risk_check overrides Fraud Prevention measures like Fraud Guard, Geo Permissions etc per verification attempt basis, allowing Verify to block traffic considered fraudulent if enabled or bypass active protections if disabled. Can be: <c>enable</c>(default) or <c>disable</c>. For SMS channel only.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<MessageEnumRiskCheck>))]
public sealed record MessageEnumRiskCheck : StringEnum<MessageEnumRiskCheck>
{
    private MessageEnumRiskCheck(string value) : base(value)
    {
    }

    public static readonly MessageEnumRiskCheck Enable = new("enable");

    public static readonly MessageEnumRiskCheck Disable = new("disable");

    public static MessageEnumRiskCheck FromValue(string value) => FromValueCore(value);
}
