using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The type of document.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<DocumentType>))]
public sealed record DocumentType : StringEnum<DocumentType>
{
    private DocumentType(string value) : base(value)
    {
    }

    public static readonly DocumentType ScLetterOfAuthorization = new("SC_LETTER_OF_AUTHORIZATION");

    public static readonly DocumentType CallToActionOrOptInMockup = new("CALL_TO_ACTION_OR_OPT_IN_MOCKUP");

    public static readonly DocumentType SmsCampaignTermsOfService = new("SMS_CAMPAIGN_TERMS_OF_SERVICE");

    public static readonly DocumentType SmsPrivacyPolicy = new("SMS_PRIVACY_POLICY");

    public static readonly DocumentType MigrationRequestLetter = new("MIGRATION_REQUEST_LETTER");

    public static readonly DocumentType ShortCodeLeaseReceipt = new("SHORT_CODE_LEASE_RECEIPT");

    public static readonly DocumentType AdditionalSupportingDocuments1 = new("ADDITIONAL_SUPPORTING_DOCUMENTS_1");

    public static readonly DocumentType AdditionalSupportingDocuments2 = new("ADDITIONAL_SUPPORTING_DOCUMENTS_2");

    public static readonly DocumentType AdditionalSupportingDocuments3 = new("ADDITIONAL_SUPPORTING_DOCUMENTS_3");

    public static readonly DocumentType AdditionalSupportingDocuments4 = new("ADDITIONAL_SUPPORTING_DOCUMENTS_4");

    public static readonly DocumentType AdditionalSupportingDocuments5 = new("ADDITIONAL_SUPPORTING_DOCUMENTS_5");

    public static readonly DocumentType AdditionalSupportingDocuments6 = new("ADDITIONAL_SUPPORTING_DOCUMENTS_6");

    public static readonly DocumentType AdditionalSupportingDocuments7 = new("ADDITIONAL_SUPPORTING_DOCUMENTS_7");

    public static readonly DocumentType AdditionalSupportingDocuments8 = new("ADDITIONAL_SUPPORTING_DOCUMENTS_8");

    public static readonly DocumentType AdditionalSupportingDocuments9 = new("ADDITIONAL_SUPPORTING_DOCUMENTS_9");

    public static readonly DocumentType AdditionalSupportingDocuments10 = new("ADDITIONAL_SUPPORTING_DOCUMENTS_10");

    public static DocumentType FromValue(string value) => FromValueCore(value);
}
