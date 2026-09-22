using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<AnnotationEnumAnsweredBy>))]
public sealed record AnnotationEnumAnsweredBy : StringEnum<AnnotationEnumAnsweredBy>
{
    private AnnotationEnumAnsweredBy(string value) : base(value)
    {
    }

    public static readonly AnnotationEnumAnsweredBy UnknownAnsweredBy = new("unknown_answered_by");

    public static readonly AnnotationEnumAnsweredBy Human = new("human");

    public static readonly AnnotationEnumAnsweredBy Machine = new("machine");

    public static AnnotationEnumAnsweredBy FromValue(string value) => FromValueCore(value);
}
