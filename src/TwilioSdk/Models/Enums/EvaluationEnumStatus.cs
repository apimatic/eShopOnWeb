using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The compliance status of the Evaluation resource.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<EvaluationEnumStatus>))]
public sealed record EvaluationEnumStatus : StringEnum<EvaluationEnumStatus>
{
    private EvaluationEnumStatus(string value) : base(value)
    {
    }

    public static readonly EvaluationEnumStatus Compliant = new("compliant");

    public static readonly EvaluationEnumStatus Noncompliant = new("noncompliant");

    public static EvaluationEnumStatus FromValue(string value) => FromValueCore(value);
}
