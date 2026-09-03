using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The preference for Proxy Number selection in the Service instance. Can be: <c>prefer-sticky</c> or <c>avoid-sticky</c>. <c>prefer-sticky</c> means that we will try and select the same Proxy Number for a given participant if they have previous <see href="https://www.twilio.com/docs/proxy/api/session">Sessions</see>, but we will not fail if that Proxy Number cannot be used.  <c>avoid-sticky</c> means that we will try to use different Proxy Numbers as long as that is possible within a given pool rather than try and use a previously assigned number.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ServiceEnumNumberSelectionBehavior>))]
public sealed record ServiceEnumNumberSelectionBehavior : StringEnum<ServiceEnumNumberSelectionBehavior>
{
    private ServiceEnumNumberSelectionBehavior(string value) : base(value)
    {
    }

    public static readonly ServiceEnumNumberSelectionBehavior AvoidSticky = new("avoid-sticky");

    public static readonly ServiceEnumNumberSelectionBehavior PreferSticky = new("prefer-sticky");

    public static ServiceEnumNumberSelectionBehavior FromValue(string value) => FromValueCore(value);
}
