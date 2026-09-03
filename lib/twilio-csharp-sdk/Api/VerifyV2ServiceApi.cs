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

namespace Twilio.Api;

public sealed class VerifyV2ServiceApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VerifyV2ServiceApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Verification Service.
    /// </summary>
    /// <param name="friendlyName"></param>
    /// <param name="codeLength"></param>
    /// <param name="lookupEnabled"></param>
    /// <param name="skipSmsToLandlines"></param>
    /// <param name="dtmfInputRequired"></param>
    /// <param name="ttsName"></param>
    /// <param name="psd2Enabled"></param>
    /// <param name="doNotShareWarningEnabled"></param>
    /// <param name="customCodeEnabled"></param>
    /// <param name="pushIncludeDate"></param>
    /// <param name="pushApnCredentialSid"></param>
    /// <param name="pushFcmCredentialSid"></param>
    /// <param name="totpIssuer"></param>
    /// <param name="totpTimeStep"></param>
    /// <param name="totpCodeLength"></param>
    /// <param name="totpSkew"></param>
    /// <param name="defaultTemplateSid"></param>
    /// <param name="whatsappMsgServiceSid"></param>
    /// <param name="whatsappFrom"></param>
    /// <param name="passkeysRelyingPartyId"></param>
    /// <param name="passkeysRelyingPartyName"></param>
    /// <param name="passkeysRelyingPartyOrigins"></param>
    /// <param name="passkeysAuthenticatorAttachment"></param>
    /// <param name="passkeysDiscoverableCredentials"></param>
    /// <param name="passkeysUserVerification"></param>
    /// <param name="verifyEventSubscriptionEnabled"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2Service"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Verification Service.
    /// </remarks>
    public Task<VerifyV2Service> CreateService2(string friendlyName,
        int? codeLength,
        bool? lookupEnabled,
        bool? skipSmsToLandlines,
        bool? dtmfInputRequired,
        string? ttsName,
        bool? psd2Enabled,
        bool? doNotShareWarningEnabled,
        bool? customCodeEnabled,
        bool? pushIncludeDate,
        string? pushApnCredentialSid,
        string? pushFcmCredentialSid,
        string? totpIssuer,
        int? totpTimeStep,
        int? totpCodeLength,
        int? totpSkew,
        string? defaultTemplateSid,
        string? whatsappMsgServiceSid,
        string? whatsappFrom,
        string? passkeysRelyingPartyId,
        string? passkeysRelyingPartyName,
        string? passkeysRelyingPartyOrigins,
        string? passkeysAuthenticatorAttachment,
        string? passkeysDiscoverableCredentials,
        string? passkeysUserVerification,
        bool? verifyEventSubscriptionEnabled,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("CodeLength", codeLength),
                    new Param("LookupEnabled", lookupEnabled),
                    new Param("SkipSmsToLandlines", skipSmsToLandlines),
                    new Param("DtmfInputRequired", dtmfInputRequired),
                    new Param("TtsName", ttsName),
                    new Param("Psd2Enabled", psd2Enabled),
                    new Param("DoNotShareWarningEnabled", doNotShareWarningEnabled),
                    new Param("CustomCodeEnabled", customCodeEnabled),
                    new Param("Push.IncludeDate", pushIncludeDate),
                    new Param("Push.ApnCredentialSid", pushApnCredentialSid),
                    new Param("Push.FcmCredentialSid", pushFcmCredentialSid),
                    new Param("Totp.Issuer", totpIssuer),
                    new Param("Totp.TimeStep", totpTimeStep),
                    new Param("Totp.CodeLength", totpCodeLength),
                    new Param("Totp.Skew", totpSkew),
                    new Param("DefaultTemplateSid", defaultTemplateSid),
                    new Param("Whatsapp.MsgServiceSid", whatsappMsgServiceSid),
                    new Param("Whatsapp.From", whatsappFrom),
                    new Param("Passkeys.RelyingParty.Id", passkeysRelyingPartyId),
                    new Param("Passkeys.RelyingParty.Name", passkeysRelyingPartyName),
                    new Param("Passkeys.RelyingParty.Origins", passkeysRelyingPartyOrigins),
                    new Param("Passkeys.AuthenticatorAttachment", passkeysAuthenticatorAttachment),
                    new Param("Passkeys.DiscoverableCredentials", passkeysDiscoverableCredentials),
                    new Param("Passkeys.UserVerification", passkeysUserVerification),
                    new Param("VerifyEventSubscriptionEnabled", verifyEventSubscriptionEnabled)]),
            JsonResponse.Create<VerifyV2Service>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a specific Verification Service Instance.
    /// </summary>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Verification Service resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a specific Verification Service Instance.
    /// </remarks>
    public Task DeleteService2(string sid, RequestOptions? requestOptions = null, CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{Sid}"),
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
    /// Fetch specific Verification Service Instance.
    /// </summary>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Verification Service resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2Service"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch specific Verification Service Instance.
    /// </remarks>
    public Task<VerifyV2Service> FetchService2(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VerifyV2Service>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Verification Services for an account.
    /// </summary>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListServiceResponse1"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Verification Services for an account.
    /// </remarks>
    public Task<ListServiceResponse1> ListService2(long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services"),
            [],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListServiceResponse1>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update a specific Verification Service.
    /// </summary>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Service resource to update.</param>
    /// <param name="friendlyName"></param>
    /// <param name="codeLength"></param>
    /// <param name="lookupEnabled"></param>
    /// <param name="skipSmsToLandlines"></param>
    /// <param name="dtmfInputRequired"></param>
    /// <param name="ttsName"></param>
    /// <param name="psd2Enabled"></param>
    /// <param name="doNotShareWarningEnabled"></param>
    /// <param name="customCodeEnabled"></param>
    /// <param name="pushIncludeDate"></param>
    /// <param name="pushApnCredentialSid"></param>
    /// <param name="pushFcmCredentialSid"></param>
    /// <param name="totpIssuer"></param>
    /// <param name="totpTimeStep"></param>
    /// <param name="totpCodeLength"></param>
    /// <param name="totpSkew"></param>
    /// <param name="defaultTemplateSid"></param>
    /// <param name="whatsappMsgServiceSid"></param>
    /// <param name="whatsappFrom"></param>
    /// <param name="passkeysRelyingPartyId"></param>
    /// <param name="passkeysRelyingPartyName"></param>
    /// <param name="passkeysRelyingPartyOrigins"></param>
    /// <param name="passkeysAuthenticatorAttachment"></param>
    /// <param name="passkeysDiscoverableCredentials"></param>
    /// <param name="passkeysUserVerification"></param>
    /// <param name="verifyEventSubscriptionEnabled"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2Service"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update a specific Verification Service.
    /// </remarks>
    public Task<VerifyV2Service> UpdateService2(string sid,
        string? friendlyName,
        int? codeLength,
        bool? lookupEnabled,
        bool? skipSmsToLandlines,
        bool? dtmfInputRequired,
        string? ttsName,
        bool? psd2Enabled,
        bool? doNotShareWarningEnabled,
        bool? customCodeEnabled,
        bool? pushIncludeDate,
        string? pushApnCredentialSid,
        string? pushFcmCredentialSid,
        string? totpIssuer,
        int? totpTimeStep,
        int? totpCodeLength,
        int? totpSkew,
        string? defaultTemplateSid,
        string? whatsappMsgServiceSid,
        string? whatsappFrom,
        string? passkeysRelyingPartyId,
        string? passkeysRelyingPartyName,
        string? passkeysRelyingPartyOrigins,
        string? passkeysAuthenticatorAttachment,
        string? passkeysDiscoverableCredentials,
        string? passkeysUserVerification,
        bool? verifyEventSubscriptionEnabled,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("CodeLength", codeLength),
                    new Param("LookupEnabled", lookupEnabled),
                    new Param("SkipSmsToLandlines", skipSmsToLandlines),
                    new Param("DtmfInputRequired", dtmfInputRequired),
                    new Param("TtsName", ttsName),
                    new Param("Psd2Enabled", psd2Enabled),
                    new Param("DoNotShareWarningEnabled", doNotShareWarningEnabled),
                    new Param("CustomCodeEnabled", customCodeEnabled),
                    new Param("Push.IncludeDate", pushIncludeDate),
                    new Param("Push.ApnCredentialSid", pushApnCredentialSid),
                    new Param("Push.FcmCredentialSid", pushFcmCredentialSid),
                    new Param("Totp.Issuer", totpIssuer),
                    new Param("Totp.TimeStep", totpTimeStep),
                    new Param("Totp.CodeLength", totpCodeLength),
                    new Param("Totp.Skew", totpSkew),
                    new Param("DefaultTemplateSid", defaultTemplateSid),
                    new Param("Whatsapp.MsgServiceSid", whatsappMsgServiceSid),
                    new Param("Whatsapp.From", whatsappFrom),
                    new Param("Passkeys.RelyingParty.Id", passkeysRelyingPartyId),
                    new Param("Passkeys.RelyingParty.Name", passkeysRelyingPartyName),
                    new Param("Passkeys.RelyingParty.Origins", passkeysRelyingPartyOrigins),
                    new Param("Passkeys.AuthenticatorAttachment", passkeysAuthenticatorAttachment),
                    new Param("Passkeys.DiscoverableCredentials", passkeysDiscoverableCredentials),
                    new Param("Passkeys.UserVerification", passkeysUserVerification),
                    new Param("VerifyEventSubscriptionEnabled", verifyEventSubscriptionEnabled)]),
            JsonResponse.Create<VerifyV2Service>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
