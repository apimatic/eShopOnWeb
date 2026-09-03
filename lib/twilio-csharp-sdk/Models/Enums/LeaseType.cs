using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<LeaseType>))]
public sealed record LeaseType : StringEnum<LeaseType>
{
    private LeaseType(string value) : base(value)
    {
    }

    public static readonly LeaseType Random = new("RANDOM");

    public static readonly LeaseType Vanity = new("VANITY");

    public static readonly LeaseType SelfLeased = new("SELF_LEASED");

    public static LeaseType FromValue(string value) => FromValueCore(value);
}
