using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the authorization document. Can be: <c>opened</c>, <c>signing</c>, <c>signed</c>, <c>canceled</c>, or <c>failed</c>., Status of an instance resource. It can hold one of the values: 1. opened 2. signing, 3. signed LOA, 4. canceled, 5. failed. See the section entitled <see href="https://www.twilio.com/docs/phone-numbers/hosted-numbers/hosted-numbers-api/authorization-document-resource#status-values">Status Values</see> for more information on each of these statuses.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AuthorizationDocumentEnumStatus>))]
public sealed record AuthorizationDocumentEnumStatus : StringEnum<AuthorizationDocumentEnumStatus>
{
    private AuthorizationDocumentEnumStatus(string value) : base(value)
    {
    }

    public static readonly AuthorizationDocumentEnumStatus Opened = new("opened");

    public static readonly AuthorizationDocumentEnumStatus Signing = new("signing");

    public static readonly AuthorizationDocumentEnumStatus Signed = new("signed");

    public static readonly AuthorizationDocumentEnumStatus Canceled = new("canceled");

    public static readonly AuthorizationDocumentEnumStatus Failed = new("failed");

    public static AuthorizationDocumentEnumStatus FromValue(string value) => FromValueCore(value);
}
