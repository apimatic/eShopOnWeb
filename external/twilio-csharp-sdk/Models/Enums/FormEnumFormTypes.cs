using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The Type of this Form. Currently only <c>form-push</c> is supported.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<FormEnumFormTypes>))]
public sealed record FormEnumFormTypes : StringEnum<FormEnumFormTypes>
{
    private FormEnumFormTypes(string value) : base(value)
    {
    }

    public static readonly FormEnumFormTypes FormPush = new("form-push");

    public static FormEnumFormTypes FromValue(string value) => FromValueCore(value);
}
