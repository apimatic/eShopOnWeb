using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<AuthenticationActionType>))]
public sealed record AuthenticationActionType : StringEnum<AuthenticationActionType>
{
    private AuthenticationActionType(string value) : base(value)
    {
    }

    public static readonly AuthenticationActionType CopyCode = new("COPY_CODE");

    public static AuthenticationActionType FromValue(string value) => FromValueCore(value);
}
