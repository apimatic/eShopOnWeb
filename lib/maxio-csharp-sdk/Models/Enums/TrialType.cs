using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

/// <summary>
/// Indicates how a trial is handled when the trial period ends and there is no credit card on file. For <c>no_obligation</c>, the subscription transitions to a Trial Ended state. Maxio will not send any emails or statements. For <c>payment_expected</c>, the subscription transitions to a Past Due state. Maxio will send normal dunning emails and statements according to your other settings.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<TrialType>))]
public sealed record TrialType : StringEnum<TrialType>
{
    private TrialType(string value) : base(value)
    {
    }

    public static readonly TrialType NoObligation = new("no_obligation");

    public static readonly TrialType PaymentExpected = new("payment_expected");

    public static TrialType FromValue(string value) => FromValueCore(value);
}
