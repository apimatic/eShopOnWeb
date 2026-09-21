using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the Flex onboarding. Can be: <c>ok</c>, <c>inprogress</c>,<c>notstarted</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ConfigurationEnumStatus>))]
public sealed record ConfigurationEnumStatus : StringEnum<ConfigurationEnumStatus>
{
    private ConfigurationEnumStatus(string value) : base(value)
    {
    }

    public static readonly ConfigurationEnumStatus Ok = new("ok");

    public static readonly ConfigurationEnumStatus Inprogress = new("inprogress");

    public static readonly ConfigurationEnumStatus Notstarted = new("notstarted");

    public static ConfigurationEnumStatus FromValue(string value) => FromValueCore(value);
}
