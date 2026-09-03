using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Request;
using TwilioSdk.Core.Response;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Api;

public sealed class MessagingV1TollfreeVerificationApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal MessagingV1TollfreeVerificationApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a tollfree verification
    /// </summary>
    /// <param name="businessName"></param>
    /// <param name="businessWebsite"></param>
    /// <param name="notificationEmail"></param>
    /// <param name="useCaseCategories"></param>
    /// <param name="useCaseSummary"></param>
    /// <param name="productionMessageSample"></param>
    /// <param name="optInImageUrls"></param>
    /// <param name="optInType"></param>
    /// <param name="messageVolume"></param>
    /// <param name="tollfreePhoneNumberSid"></param>
    /// <param name="customerProfileSid"></param>
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
    /// <param name="externalReferenceId"></param>
    /// <param name="businessRegistrationNumber"></param>
    /// <param name="businessRegistrationAuthority"></param>
    /// <param name="businessRegistrationCountry"></param>
    /// <param name="businessType"></param>
    /// <param name="businessRegistrationPhoneNumber"></param>
    /// <param name="doingBusinessAs"></param>
    /// <param name="optInConfirmationMessage"></param>
    /// <param name="helpMessageSample"></param>
    /// <param name="privacyPolicyUrl"></param>
    /// <param name="termsAndConditionsUrl"></param>
    /// <param name="ageGatedContent"></param>
    /// <param name="optInKeywords"></param>
    /// <param name="vettingProvider"></param>
    /// <param name="vettingId"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MessagingV1TollfreeVerification"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a tollfree verification
    /// </remarks>
    public Task<MessagingV1TollfreeVerification> CreateTollfreeVerification(string businessName,
        string businessWebsite,
        string notificationEmail,
        IReadOnlyList<TollfreeVerificationEnumUseCaseCategory?> useCaseCategories,
        string useCaseSummary,
        string productionMessageSample,
        IReadOnlyList<string> optInImageUrls,
        TollfreeVerificationEnumOptInType optInType,
        string messageVolume,
        string tollfreePhoneNumberSid,
        string? customerProfileSid,
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
        string? externalReferenceId,
        string? businessRegistrationNumber,
        TollfreeVerificationEnumBusinessRegistrationAuthority? businessRegistrationAuthority,
        string? businessRegistrationCountry,
        TollfreeVerificationEnumBusinessType? businessType,
        string? businessRegistrationPhoneNumber,
        string? doingBusinessAs,
        string? optInConfirmationMessage,
        string? helpMessageSample,
        string? privacyPolicyUrl,
        string? termsAndConditionsUrl,
        bool? ageGatedContent,
        IReadOnlyList<string>? optInKeywords,
        TollfreeVerificationEnumVettingProvider? vettingProvider,
        string? vettingId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Tollfree/Verifications"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("BusinessName", businessName),
                    new Param("BusinessWebsite", businessWebsite),
                    new Param("NotificationEmail", notificationEmail),
                    new Param("UseCaseCategories", useCaseCategories),
                    new Param("UseCaseSummary", useCaseSummary),
                    new Param("ProductionMessageSample", productionMessageSample),
                    new Param("OptInImageUrls", optInImageUrls),
                    new Param("OptInType", optInType),
                    new Param("MessageVolume", messageVolume),
                    new Param("TollfreePhoneNumberSid", tollfreePhoneNumberSid),
                    new Param("CustomerProfileSid", customerProfileSid),
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
                    new Param("ExternalReferenceId", externalReferenceId),
                    new Param("BusinessRegistrationNumber", businessRegistrationNumber),
                    new Param("BusinessRegistrationAuthority", businessRegistrationAuthority),
                    new Param("BusinessRegistrationCountry", businessRegistrationCountry),
                    new Param("BusinessType", businessType),
                    new Param("BusinessRegistrationPhoneNumber", businessRegistrationPhoneNumber),
                    new Param("DoingBusinessAs", doingBusinessAs),
                    new Param("OptInConfirmationMessage", optInConfirmationMessage),
                    new Param("HelpMessageSample", helpMessageSample),
                    new Param("PrivacyPolicyUrl", privacyPolicyUrl),
                    new Param("TermsAndConditionsUrl", termsAndConditionsUrl),
                    new Param("AgeGatedContent", ageGatedContent),
                    new Param("OptInKeywords", optInKeywords),
                    new Param("VettingProvider", vettingProvider),
                    new Param("VettingId", vettingId)]),
            JsonResponse.Create<MessagingV1TollfreeVerification>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a tollfree verification
    /// </summary>
    /// <param name="sid">The unique string to identify Tollfree Verification.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a tollfree verification
    /// </remarks>
    public Task DeleteTollfreeVerification(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Tollfree/Verifications/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a tollfree verification
    /// </summary>
    /// <param name="sid">A unique string identifying a Tollfree Verification.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MessagingV1TollfreeVerification"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a tollfree verification
    /// </remarks>
    public Task<MessagingV1TollfreeVerification> FetchTollfreeVerification(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Tollfree/Verifications/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<MessagingV1TollfreeVerification>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// List tollfree verifications
    /// </summary>
    /// <param name="tollfreePhoneNumberSid">The SID of the Phone Number associated with the Tollfree Verification.</param>
    /// <param name="status">The compliance status of the Tollfree Verification record.</param>
    /// <param name="externalReferenceId">Customer supplied reference id for the Tollfree Verification record.</param>
    /// <param name="includeSubAccounts">Whether to include Tollfree Verifications from sub accounts in list response.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="trustProductSid">The trust product sids / tollfree bundle sids of tollfree verifications</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListTollfreeVerificationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// List tollfree verifications
    /// </remarks>
    public Task<ListTollfreeVerificationResponse> ListTollfreeVerification(string? tollfreePhoneNumberSid,
        TollfreeVerificationEnumStatus? status,
        string? externalReferenceId,
        bool? includeSubAccounts,
        long? pageSize,
        int? page,
        string? pageToken,
        IReadOnlyList<string>? trustProductSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Tollfree/Verifications"),
            [],
            [new Param("TollfreePhoneNumberSid", tollfreePhoneNumberSid),
                new Param("Status", status),
                new Param("ExternalReferenceId", externalReferenceId),
                new Param("IncludeSubAccounts", includeSubAccounts),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken),
                new Param("TrustProductSid", trustProductSid)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListTollfreeVerificationResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Edit a tollfree verification
    /// </summary>
    /// <param name="sid">The unique string to identify Tollfree Verification.</param>
    /// <param name="businessName"></param>
    /// <param name="businessWebsite"></param>
    /// <param name="notificationEmail"></param>
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
    /// <param name="editReason"></param>
    /// <param name="businessRegistrationNumber"></param>
    /// <param name="businessRegistrationAuthority"></param>
    /// <param name="businessRegistrationCountry"></param>
    /// <param name="businessType"></param>
    /// <param name="businessRegistrationPhoneNumber"></param>
    /// <param name="doingBusinessAs"></param>
    /// <param name="optInConfirmationMessage"></param>
    /// <param name="helpMessageSample"></param>
    /// <param name="privacyPolicyUrl"></param>
    /// <param name="termsAndConditionsUrl"></param>
    /// <param name="ageGatedContent"></param>
    /// <param name="optInKeywords"></param>
    /// <param name="vettingProvider"></param>
    /// <param name="vettingId"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MessagingV1TollfreeVerification"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Edit a tollfree verification
    /// </remarks>
    public Task<MessagingV1TollfreeVerification> UpdateTollfreeVerification(string sid,
        string? businessName,
        string? businessWebsite,
        string? notificationEmail,
        IReadOnlyList<TollfreeVerificationEnumUseCaseCategory?>? useCaseCategories,
        string? useCaseSummary,
        string? productionMessageSample,
        IReadOnlyList<string>? optInImageUrls,
        TollfreeVerificationEnumOptInType? optInType,
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
        string? editReason,
        string? businessRegistrationNumber,
        TollfreeVerificationEnumBusinessRegistrationAuthority? businessRegistrationAuthority,
        string? businessRegistrationCountry,
        TollfreeVerificationEnumBusinessType? businessType,
        string? businessRegistrationPhoneNumber,
        string? doingBusinessAs,
        string? optInConfirmationMessage,
        string? helpMessageSample,
        string? privacyPolicyUrl,
        string? termsAndConditionsUrl,
        bool? ageGatedContent,
        IReadOnlyList<string>? optInKeywords,
        TollfreeVerificationEnumVettingProvider? vettingProvider,
        string? vettingId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Tollfree/Verifications/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("BusinessName", businessName),
                    new Param("BusinessWebsite", businessWebsite),
                    new Param("NotificationEmail", notificationEmail),
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
                    new Param("EditReason", editReason),
                    new Param("BusinessRegistrationNumber", businessRegistrationNumber),
                    new Param("BusinessRegistrationAuthority", businessRegistrationAuthority),
                    new Param("BusinessRegistrationCountry", businessRegistrationCountry),
                    new Param("BusinessType", businessType),
                    new Param("BusinessRegistrationPhoneNumber", businessRegistrationPhoneNumber),
                    new Param("DoingBusinessAs", doingBusinessAs),
                    new Param("OptInConfirmationMessage", optInConfirmationMessage),
                    new Param("HelpMessageSample", helpMessageSample),
                    new Param("PrivacyPolicyUrl", privacyPolicyUrl),
                    new Param("TermsAndConditionsUrl", termsAndConditionsUrl),
                    new Param("AgeGatedContent", ageGatedContent),
                    new Param("OptInKeywords", optInKeywords),
                    new Param("VettingProvider", vettingProvider),
                    new Param("VettingId", vettingId)]),
            JsonResponse.Create<MessagingV1TollfreeVerification>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
