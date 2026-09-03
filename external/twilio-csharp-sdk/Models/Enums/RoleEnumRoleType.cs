using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The type of role. Can be: <c>conversation</c> for <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> roles or <c>service</c> for <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> roles.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RoleEnumRoleType>))]
public sealed record RoleEnumRoleType : StringEnum<RoleEnumRoleType>
{
    private RoleEnumRoleType(string value) : base(value)
    {
    }

    public static readonly RoleEnumRoleType Conversation = new("conversation");

    public static readonly RoleEnumRoleType Service = new("service");

    public static RoleEnumRoleType FromValue(string value) => FromValueCore(value);
}
