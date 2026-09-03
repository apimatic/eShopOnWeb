using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type10>))]
public sealed record Type10 : StringEnum<Type10>
{
    private Type10(string value) : base(value)
    {
    }

    public static readonly Type10 Text = new("TEXT");

    public static Type10 FromValue(string value) => FromValueCore(value);
}
