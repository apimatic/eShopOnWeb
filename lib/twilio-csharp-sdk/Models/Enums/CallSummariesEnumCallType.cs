using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CallSummariesEnumCallType>))]
public sealed record CallSummariesEnumCallType : StringEnum<CallSummariesEnumCallType>
{
    private CallSummariesEnumCallType(string value) : base(value)
    {
    }

    public static readonly CallSummariesEnumCallType Carrier = new("carrier");

    public static readonly CallSummariesEnumCallType Sip = new("sip");

    public static readonly CallSummariesEnumCallType Trunking = new("trunking");

    public static readonly CallSummariesEnumCallType Client = new("client");

    public static readonly CallSummariesEnumCallType Whatsapp = new("whatsapp");

    public static CallSummariesEnumCallType FromValue(string value) => FromValueCore(value);
}
