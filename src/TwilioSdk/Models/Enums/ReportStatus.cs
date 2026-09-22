using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the report.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ReportStatus>))]
public sealed record ReportStatus : StringEnum<ReportStatus>
{
    private ReportStatus(string value) : base(value)
    {
    }

    public static readonly ReportStatus Created = new("created");

    public static readonly ReportStatus Running = new("running");

    public static readonly ReportStatus Completed = new("completed");

    public static ReportStatus FromValue(string value) => FromValueCore(value);
}
