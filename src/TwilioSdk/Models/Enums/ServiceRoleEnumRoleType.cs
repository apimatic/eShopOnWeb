using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The type of role. Can be: <c>conversation</c> for <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> roles or <c>service</c> for <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> roles.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ServiceRoleEnumRoleType>))]
public sealed record ServiceRoleEnumRoleType : StringEnum<ServiceRoleEnumRoleType>
{
    private ServiceRoleEnumRoleType(string value) : base(value)
    {
    }

    public static readonly ServiceRoleEnumRoleType Conversation = new("conversation");

    public static readonly ServiceRoleEnumRoleType Service = new("service");

    public static ServiceRoleEnumRoleType FromValue(string value) => FromValueCore(value);
}
