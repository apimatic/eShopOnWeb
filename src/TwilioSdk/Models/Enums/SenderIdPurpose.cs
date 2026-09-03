using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Purpose for using Sender ID
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SenderIdPurpose>))]
public sealed record SenderIdPurpose : StringEnum<SenderIdPurpose>
{
    private SenderIdPurpose(string value) : base(value)
    {
    }

    public static readonly SenderIdPurpose Transactional = new("TRANSACTIONAL");

    public static readonly SenderIdPurpose Promotional = new("PROMOTIONAL");

    public static SenderIdPurpose FromValue(string value) => FromValueCore(value);
}
