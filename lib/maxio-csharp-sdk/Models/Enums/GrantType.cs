using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<GrantType>))]
public sealed record GrantType : StringEnum<GrantType>
{
    private GrantType(string value) : base(value)
    {
    }

    public static readonly GrantType ClientCredentials = new("client_credentials");

    public static GrantType FromValue(string value) => FromValueCore(value);
}
