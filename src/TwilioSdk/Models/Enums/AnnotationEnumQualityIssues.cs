using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<AnnotationEnumQualityIssues>))]
public sealed record AnnotationEnumQualityIssues : StringEnum<AnnotationEnumQualityIssues>
{
    private AnnotationEnumQualityIssues(string value) : base(value)
    {
    }

    public static readonly AnnotationEnumQualityIssues UnknownQualityIssue = new("unknown_quality_issue");

    public static readonly AnnotationEnumQualityIssues NoQualityIssue = new("no_quality_issue");

    public static readonly AnnotationEnumQualityIssues LowVolume = new("low_volume");

    public static readonly AnnotationEnumQualityIssues ChoppyRobotic = new("choppy_robotic");

    public static readonly AnnotationEnumQualityIssues Echo = new("echo");

    public static readonly AnnotationEnumQualityIssues Dtmf = new("dtmf");

    public static readonly AnnotationEnumQualityIssues Latency = new("latency");

    public static readonly AnnotationEnumQualityIssues Owa = new("owa");

    public static readonly AnnotationEnumQualityIssues StaticNoise = new("static_noise");

    public static AnnotationEnumQualityIssues FromValue(string value) => FromValueCore(value);
}
