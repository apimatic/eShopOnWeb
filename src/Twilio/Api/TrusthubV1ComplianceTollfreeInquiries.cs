using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Exceptions;
using Twilio.Core.Models;
using Twilio.Core.Request;
using Twilio.Core.Response;
using Twilio.Models;
using Twilio.Models.Enums;

namespace Twilio.Api;

public sealed class TrusthubV1ComplianceTollfreeInquiries
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TrusthubV1ComplianceTollfreeInquiries(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Compliance Tollfree Verification Inquiry for the authenticated account. This is necessary to start a new embedded session.
    /// </summary>
    /// <param name="tollfreePhoneNumber"></param>
    /// <param name="notificationEmail"></param>
    /// <param name="customerProfileSid"></param>
    /// <param name="businessName"></param>
    /// <param name="businessWebsite"></param>
    /// <param name="useCaseCategories"></param>
    /// <param name="useCaseSummary"></param>
    /// <param name="productionMessageSample"></param>
    /// <param name="optInImageUrls"></param>
    /// <param name="optInType"></param>
    /// <param name="messageVolume"></param>
    /// <param name="businessStreetAddress"></param>
    /// <param name="businessStreetAddress2"></param>
    /// <param name="businessCity"></param>
    /// <param name="businessStateProvinceRegion"></param>
    /// <param name="businessPostalCode"></param>
    /// <param name="businessCountry"></param>
    /// <param name="additionalInformation"></param>
    /// <param name="businessContactFirstName"></param>
    /// <param name="businessContactLastName"></param>
    /// <param name="businessContactEmail"></param>
    /// <param name="businessContactPhone"></param>
    /// <param name="themeSetId"></param>
    /// <param name="skipMessagingUseCase"></param>
    /// <param name="businessRegistrationNumber"></param>
    /// <param name="businessRegistrationAuthority"></param>
    /// <param name="businessRegistrationCountry"></param>
    /// <param name="businessType"></param>
    /// <param name="doingBusinessAs"></param>
    /// <param name="optInConfirmationMessage"></param>
    /// <param name="helpMessageSample"></param>
    /// <param name="privacyPolicyUrl"></param>
    /// <param name="termsAndConditionsUrl"></param>
    /// <param name="ageGatedContent"></param>
    /// <param name="externalReferenceId"></param>
    /// <param name="optInKeywords"></param>
    /// <param name="vettingId"></param>
    /// <param name="vettingProvider"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TrusthubV1ComplianceTollfreeInquiry"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Compliance Tollfree Verification Inquiry for the authenticated account. This is necessary to start a new embedded session.
    /// </remarks>
    public Task<TrusthubV1ComplianceTollfreeInquiry> CreateComplianceTollfreeInquiry(string tollfreePhoneNumber,
        string notificationEmail,
        string? customerProfileSid,
        string? businessName,
        string? businessWebsite,
        IReadOnlyList<string>? useCaseCategories,
        string? useCaseSummary,
        string? productionMessageSample,
        IReadOnlyList<string>? optInImageUrls,
        ComplianceTollfreeInquiryEnumOptInType? optInType,
        string? messageVolume,
        string? businessStreetAddress,
        string? businessStreetAddress2,
        string? businessCity,
        string? businessStateProvinceRegion,
        string? businessPostalCode,
        string? businessCountry,
        string? additionalInformation,
        string? businessContactFirstName,
        string? businessContactLastName,
        string? businessContactEmail,
        string? businessContactPhone,
        string? themeSetId,
        bool? skipMessagingUseCase,
        string? businessRegistrationNumber,
        string? businessRegistrationAuthority,
        string? businessRegistrationCountry,
        TollfreeVerificationEnumBusinessType? businessType,
        string? doingBusinessAs,
        string? optInConfirmationMessage,
        string? helpMessageSample,
        string? privacyPolicyUrl,
        string? termsAndConditionsUrl,
        bool? ageGatedContent,
        string? externalReferenceId,
        IReadOnlyList<string>? optInKeywords,
        string? vettingId,
        string? vettingProvider,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/ComplianceInquiries/Tollfree/Initialize"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("TollfreePhoneNumber", tollfreePhoneNumber),
                    new Param("NotificationEmail", notificationEmail),
                    new Param("CustomerProfileSid", customerProfileSid),
                    new Param("BusinessName", businessName),
                    new Param("BusinessWebsite", businessWebsite),
                    new Param("UseCaseCategories", useCaseCategories),
                    new Param("UseCaseSummary", useCaseSummary),
                    new Param("ProductionMessageSample", productionMessageSample),
                    new Param("OptInImageUrls", optInImageUrls),
                    new Param("OptInType", optInType),
                    new Param("MessageVolume", messageVolume),
                    new Param("BusinessStreetAddress", businessStreetAddress),
                    new Param("BusinessStreetAddress2", businessStreetAddress2),
                    new Param("BusinessCity", businessCity),
                    new Param("BusinessStateProvinceRegion", businessStateProvinceRegion),
                    new Param("BusinessPostalCode", businessPostalCode),
                    new Param("BusinessCountry", businessCountry),
                    new Param("AdditionalInformation", additionalInformation),
                    new Param("BusinessContactFirstName", businessContactFirstName),
                    new Param("BusinessContactLastName", businessContactLastName),
                    new Param("BusinessContactEmail", businessContactEmail),
                    new Param("BusinessContactPhone", businessContactPhone),
                    new Param("ThemeSetId", themeSetId),
                    new Param("SkipMessagingUseCase", skipMessagingUseCase),
                    new Param("BusinessRegistrationNumber", businessRegistrationNumber),
                    new Param("BusinessRegistrationAuthority", businessRegistrationAuthority),
                    new Param("BusinessRegistrationCountry", businessRegistrationCountry),
                    new Param("BusinessType", businessType),
                    new Param("DoingBusinessAs", doingBusinessAs),
                    new Param("OptInConfirmationMessage", optInConfirmationMessage),
                    new Param("HelpMessageSample", helpMessageSample),
                    new Param("PrivacyPolicyUrl", privacyPolicyUrl),
                    new Param("TermsAndConditionsUrl", termsAndConditionsUrl),
                    new Param("AgeGatedContent", ageGatedContent),
                    new Param("ExternalReferenceId", externalReferenceId),
                    new Param("OptInKeywords", optInKeywords),
                    new Param("VettingId", vettingId),
                    new Param("VettingProvider", vettingProvider)]),
            JsonResponse.Create<TrusthubV1ComplianceTollfreeInquiry>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
