using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The set of permissions that your ConnectApp requests.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ConnectAppEnumPermission>))]
public sealed record ConnectAppEnumPermission : StringEnum<ConnectAppEnumPermission>
{
    private ConnectAppEnumPermission(string value) : base(value)
    {
    }

    public static readonly ConnectAppEnumPermission GetAll = new("get-all");

    public static readonly ConnectAppEnumPermission PostAll = new("post-all");

    public static ConnectAppEnumPermission FromValue(string value) => FromValueCore(value);
}
