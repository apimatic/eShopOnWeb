using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Describe how a user opts-in to text messages.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ComplianceTollfreeInquiryEnumOptInType>))]
public sealed record ComplianceTollfreeInquiryEnumOptInType : StringEnum<ComplianceTollfreeInquiryEnumOptInType>
{
    private ComplianceTollfreeInquiryEnumOptInType(string value) : base(value)
    {
    }

    public static readonly ComplianceTollfreeInquiryEnumOptInType Verbal = new("VERBAL");

    public static readonly ComplianceTollfreeInquiryEnumOptInType WebForm = new("WEB_FORM");

    public static readonly ComplianceTollfreeInquiryEnumOptInType PaperForm = new("PAPER_FORM");

    public static readonly ComplianceTollfreeInquiryEnumOptInType ViaText = new("VIA_TEXT");

    public static readonly ComplianceTollfreeInquiryEnumOptInType MobileQrCode = new("MOBILE_QR_CODE");

    public static ComplianceTollfreeInquiryEnumOptInType FromValue(string value) => FromValueCore(value);
}
