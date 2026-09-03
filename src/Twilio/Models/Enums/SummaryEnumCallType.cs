using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SummaryEnumCallType>))]
public sealed record SummaryEnumCallType : StringEnum<SummaryEnumCallType>
{
    private SummaryEnumCallType(string value) : base(value)
    {
    }

    public static readonly SummaryEnumCallType Carrier = new("carrier");

    public static readonly SummaryEnumCallType Sip = new("sip");

    public static readonly SummaryEnumCallType Trunking = new("trunking");

    public static readonly SummaryEnumCallType Client = new("client");

    public static readonly SummaryEnumCallType Whatsapp = new("whatsapp");

    public static SummaryEnumCallType FromValue(string value) => FromValueCore(value);
}
