using System;
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

public sealed class TrusthubV1ComplianceRegistrationInquiries
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TrusthubV1ComplianceRegistrationInquiries(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Compliance Registration Inquiry for the authenticated account. This is necessary to start a new embedded session.
    /// </summary>
    /// <param name="endUserType"></param>
    /// <param name="phoneNumberType"></param>
    /// <param name="businessIdentityType"></param>
    /// <param name="businessRegistrationAuthority"></param>
    /// <param name="businessLegalName"></param>
    /// <param name="notificationEmail"></param>
    /// <param name="acceptedNotificationReceipt"></param>
    /// <param name="businessRegistrationNumber"></param>
    /// <param name="businessWebsiteUrl"></param>
    /// <param name="friendlyName"></param>
    /// <param name="authorizedRepresentative1FirstName"></param>
    /// <param name="authorizedRepresentative1LastName"></param>
    /// <param name="authorizedRepresentative1Phone"></param>
    /// <param name="authorizedRepresentative1Email"></param>
    /// <param name="authorizedRepresentative1DateOfBirth"></param>
    /// <param name="addressStreet"></param>
    /// <param name="addressStreetSecondary"></param>
    /// <param name="addressCity"></param>
    /// <param name="addressSubdivision"></param>
    /// <param name="addressPostalCode"></param>
    /// <param name="addressCountryCode"></param>
    /// <param name="emergencyAddressStreet"></param>
    /// <param name="emergencyAddressStreetSecondary"></param>
    /// <param name="emergencyAddressCity"></param>
    /// <param name="emergencyAddressSubdivision"></param>
    /// <param name="emergencyAddressPostalCode"></param>
    /// <param name="emergencyAddressCountryCode"></param>
    /// <param name="useAddressAsEmergencyAddress"></param>
    /// <param name="fileName"></param>
    /// <param name="file"></param>
    /// <param name="firstName"></param>
    /// <param name="lastName"></param>
    /// <param name="dateOfBirth"></param>
    /// <param name="individualEmail"></param>
    /// <param name="individualPhone"></param>
    /// <param name="isIsvEmbed"></param>
    /// <param name="isvRegisteringForSelfOrTenant"></param>
    /// <param name="statusCallbackUrl"></param>
    /// <param name="themeSetId"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TrusthubV1ComplianceRegistration"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Compliance Registration Inquiry for the authenticated account. This is necessary to start a new embedded session.
    /// </remarks>
    public Task<TrusthubV1ComplianceRegistration> CreateComplianceRegistration(CustomerType endUserType,
        ComplianceRegistrationEnumPhoneNumberType phoneNumberType,
        ComplianceRegistrationEnumBusinessIdentityType? businessIdentityType,
        ComplianceRegistrationEnumBusinessRegistrationAuthority? businessRegistrationAuthority,
        string? businessLegalName,
        string? notificationEmail,
        bool? acceptedNotificationReceipt,
        string? businessRegistrationNumber,
        string? businessWebsiteUrl,
        string? friendlyName,
        string? authorizedRepresentative1FirstName,
        string? authorizedRepresentative1LastName,
        string? authorizedRepresentative1Phone,
        string? authorizedRepresentative1Email,
        string? authorizedRepresentative1DateOfBirth,
        string? addressStreet,
        string? addressStreetSecondary,
        string? addressCity,
        string? addressSubdivision,
        string? addressPostalCode,
        string? addressCountryCode,
        string? emergencyAddressStreet,
        string? emergencyAddressStreetSecondary,
        string? emergencyAddressCity,
        string? emergencyAddressSubdivision,
        string? emergencyAddressPostalCode,
        string? emergencyAddressCountryCode,
        bool? useAddressAsEmergencyAddress,
        string? fileName,
        string? file,
        string? firstName,
        string? lastName,
        string? dateOfBirth,
        string? individualEmail,
        string? individualPhone,
        bool? isIsvEmbed,
        string? isvRegisteringForSelfOrTenant,
        string? statusCallbackUrl,
        string? themeSetId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/ComplianceInquiries/Registration/RegulatoryCompliance/GB/Initialize"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("EndUserType", endUserType),
                    new Param("PhoneNumberType", phoneNumberType),
                    new Param("BusinessIdentityType", businessIdentityType),
                    new Param("BusinessRegistrationAuthority", businessRegistrationAuthority),
                    new Param("BusinessLegalName", businessLegalName),
                    new Param("NotificationEmail", notificationEmail),
                    new Param("AcceptedNotificationReceipt", acceptedNotificationReceipt),
                    new Param("BusinessRegistrationNumber", businessRegistrationNumber),
                    new Param("BusinessWebsiteUrl", businessWebsiteUrl),
                    new Param("FriendlyName", friendlyName),
                    new Param("AuthorizedRepresentative1FirstName", authorizedRepresentative1FirstName),
                    new Param("AuthorizedRepresentative1LastName", authorizedRepresentative1LastName),
                    new Param("AuthorizedRepresentative1Phone", authorizedRepresentative1Phone),
                    new Param("AuthorizedRepresentative1Email", authorizedRepresentative1Email),
                    new Param("AuthorizedRepresentative1DateOfBirth", authorizedRepresentative1DateOfBirth),
                    new Param("AddressStreet", addressStreet),
                    new Param("AddressStreetSecondary", addressStreetSecondary),
                    new Param("AddressCity", addressCity),
                    new Param("AddressSubdivision", addressSubdivision),
                    new Param("AddressPostalCode", addressPostalCode),
                    new Param("AddressCountryCode", addressCountryCode),
                    new Param("EmergencyAddressStreet", emergencyAddressStreet),
                    new Param("EmergencyAddressStreetSecondary", emergencyAddressStreetSecondary),
                    new Param("EmergencyAddressCity", emergencyAddressCity),
                    new Param("EmergencyAddressSubdivision", emergencyAddressSubdivision),
                    new Param("EmergencyAddressPostalCode", emergencyAddressPostalCode),
                    new Param("EmergencyAddressCountryCode", emergencyAddressCountryCode),
                    new Param("UseAddressAsEmergencyAddress", useAddressAsEmergencyAddress),
                    new Param("FileName", fileName),
                    new Param("File", file),
                    new Param("FirstName", firstName),
                    new Param("LastName", lastName),
                    new Param("DateOfBirth", dateOfBirth),
                    new Param("IndividualEmail", individualEmail),
                    new Param("IndividualPhone", individualPhone),
                    new Param("IsIsvEmbed", isIsvEmbed),
                    new Param("IsvRegisteringForSelfOrTenant", isvRegisteringForSelfOrTenant),
                    new Param("StatusCallbackUrl", statusCallbackUrl),
                    new Param("ThemeSetId", themeSetId)]),
            JsonResponse.Create<TrusthubV1ComplianceRegistration>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Resume a specific Regulatory Compliance Inquiry that has expired, or re-open a rejected Compliance Inquiry for editing.
    /// </summary>
    /// <param name="registrationId">The unique RegistrationId matching the Regulatory Compliance Inquiry that should be resumed or resubmitted. This value will have been returned by the initial Regulatory Compliance Inquiry creation call.</param>
    /// <param name="isIsvEmbed"></param>
    /// <param name="themeSetId"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TrusthubV1ComplianceRegistration"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Resume a specific Regulatory Compliance Inquiry that has expired, or re-open a rejected Compliance Inquiry for editing.
    /// </remarks>
    public Task<TrusthubV1ComplianceRegistration> UpdateComplianceRegistration(string registrationId,
        bool? isIsvEmbed,
        string? themeSetId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/ComplianceInquiries/Registration/{RegistrationId}/RegulatoryCompliance/GB/Initialize"),
            [new TemplateParam("RegistrationId", registrationId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("IsIsvEmbed", isIsvEmbed), new Param("ThemeSetId", themeSetId)]),
            JsonResponse.Create<TrusthubV1ComplianceRegistration>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
