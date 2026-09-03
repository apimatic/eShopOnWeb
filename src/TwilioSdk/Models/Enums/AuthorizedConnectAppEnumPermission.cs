using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The set of permissions that you authorized for the Connect App.  Can be: <c>get-all</c> or <c>post-all</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AuthorizedConnectAppEnumPermission>))]
public sealed record AuthorizedConnectAppEnumPermission : StringEnum<AuthorizedConnectAppEnumPermission>
{
    private AuthorizedConnectAppEnumPermission(string value) : base(value)
    {
    }

    public static readonly AuthorizedConnectAppEnumPermission GetAll = new("get-all");

    public static readonly AuthorizedConnectAppEnumPermission PostAll = new("post-all");

    public static AuthorizedConnectAppEnumPermission FromValue(string value) => FromValueCore(value);
}
