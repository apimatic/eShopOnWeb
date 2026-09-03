using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<VideoRoomSummaryEnumCreatedMethod>))]
public sealed record VideoRoomSummaryEnumCreatedMethod : StringEnum<VideoRoomSummaryEnumCreatedMethod>
{
    private VideoRoomSummaryEnumCreatedMethod(string value) : base(value)
    {
    }

    public static readonly VideoRoomSummaryEnumCreatedMethod Sdk = new("sdk");

    public static readonly VideoRoomSummaryEnumCreatedMethod AdHoc = new("ad_hoc");

    public static readonly VideoRoomSummaryEnumCreatedMethod Api = new("api");

    public static VideoRoomSummaryEnumCreatedMethod FromValue(string value) => FromValueCore(value);
}
