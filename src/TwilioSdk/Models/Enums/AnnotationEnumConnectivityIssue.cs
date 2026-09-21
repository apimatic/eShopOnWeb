using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<AnnotationEnumConnectivityIssue>))]
public sealed record AnnotationEnumConnectivityIssue : StringEnum<AnnotationEnumConnectivityIssue>
{
    private AnnotationEnumConnectivityIssue(string value) : base(value)
    {
    }

    public static readonly AnnotationEnumConnectivityIssue UnknownConnectivityIssue = new("unknown_connectivity_issue");

    public static readonly AnnotationEnumConnectivityIssue NoConnectivityIssue = new("no_connectivity_issue");

    public static readonly AnnotationEnumConnectivityIssue InvalidNumber = new("invalid_number");

    public static readonly AnnotationEnumConnectivityIssue CallerId = new("caller_id");

    public static readonly AnnotationEnumConnectivityIssue DroppedCall = new("dropped_call");

    public static readonly AnnotationEnumConnectivityIssue NumberReachability = new("number_reachability");

    public static AnnotationEnumConnectivityIssue FromValue(string value) => FromValueCore(value);
}
