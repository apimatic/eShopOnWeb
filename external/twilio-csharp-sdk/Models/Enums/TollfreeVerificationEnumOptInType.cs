using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Describe how a user opts-in to text messages.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<TollfreeVerificationEnumOptInType>))]
public sealed record TollfreeVerificationEnumOptInType : StringEnum<TollfreeVerificationEnumOptInType>
{
    private TollfreeVerificationEnumOptInType(string value) : base(value)
    {
    }

    public static readonly TollfreeVerificationEnumOptInType Verbal = new("VERBAL");

    public static readonly TollfreeVerificationEnumOptInType WebForm = new("WEB_FORM");

    public static readonly TollfreeVerificationEnumOptInType PaperForm = new("PAPER_FORM");

    public static readonly TollfreeVerificationEnumOptInType ViaText = new("VIA_TEXT");

    public static readonly TollfreeVerificationEnumOptInType MobileQrCode = new("MOBILE_QR_CODE");

    public static readonly TollfreeVerificationEnumOptInType Import = new("IMPORT");

    public static readonly TollfreeVerificationEnumOptInType ImportPleaseReplace = new("IMPORT_PLEASE_REPLACE");

    public static TollfreeVerificationEnumOptInType FromValue(string value) => FromValueCore(value);
}
