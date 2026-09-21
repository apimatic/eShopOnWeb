using System;
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

public sealed class Api20100401Domain
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Domain(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Domain
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that will create the resource.</param>
    /// <param name="domainName"></param>
    /// <param name="friendlyName"></param>
    /// <param name="voiceUrl"></param>
    /// <param name="voiceMethod"></param>
    /// <param name="voiceFallbackUrl"></param>
    /// <param name="voiceFallbackMethod"></param>
    /// <param name="voiceStatusCallbackUrl"></param>
    /// <param name="voiceStatusCallbackMethod"></param>
    /// <param name="sipRegistration"></param>
    /// <param name="emergencyCallingEnabled"></param>
    /// <param name="secure"></param>
    /// <param name="byocTrunkSid"></param>
    /// <param name="emergencyCallerSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountSipSipDomain"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Domain
    /// </remarks>
    public Task<ApiV2010AccountSipSipDomain> CreateSipDomain(string accountSid,
        string domainName,
        string? friendlyName,
        string? voiceUrl,
        VoiceMethod7? voiceMethod,
        string? voiceFallbackUrl,
        VoiceFallbackMethod7? voiceFallbackMethod,
        string? voiceStatusCallbackUrl,
        VoiceStatusCallbackMethod1? voiceStatusCallbackMethod,
        bool? sipRegistration,
        bool? emergencyCallingEnabled,
        bool? secure,
        string? byocTrunkSid,
        string? emergencyCallerSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/Domains.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("DomainName", domainName),
                    new Param("FriendlyName", friendlyName),
                    new Param("VoiceUrl", voiceUrl),
                    new Param("VoiceMethod", voiceMethod),
                    new Param("VoiceFallbackUrl", voiceFallbackUrl),
                    new Param("VoiceFallbackMethod", voiceFallbackMethod),
                    new Param("VoiceStatusCallbackUrl", voiceStatusCallbackUrl),
                    new Param("VoiceStatusCallbackMethod", voiceStatusCallbackMethod),
                    new Param("SipRegistration", sipRegistration),
                    new Param("EmergencyCallingEnabled", emergencyCallingEnabled),
                    new Param("Secure", secure),
                    new Param("ByocTrunkSid", byocTrunkSid),
                    new Param("EmergencyCallerSid", emergencyCallerSid)]),
            JsonResponse.Create<ApiV2010AccountSipSipDomain>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete an instance of a Domain
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the SipDomain resources to delete.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the SipDomain resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete an instance of a Domain
    /// </remarks>
    public Task DeleteSipDomain(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/Domains/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
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
    /// Fetch an instance of a Domain
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the SipDomain resource to fetch.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the SipDomain resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountSipSipDomain"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch an instance of a Domain
    /// </remarks>
    public Task<ApiV2010AccountSipSipDomain> FetchSipDomain(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/Domains/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountSipSipDomain>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of domains belonging to the account used to make the request
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the SipDomain resources to read.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListSipDomainResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of domains belonging to the account used to make the request
    /// </remarks>
    public Task<ListSipDomainResponse> ListSipDomain(string accountSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/Domains.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListSipDomainResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update the attributes of a domain
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the SipDomain resource to update.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the SipDomain resource to update.</param>
    /// <param name="friendlyName"></param>
    /// <param name="voiceFallbackMethod"></param>
    /// <param name="voiceFallbackUrl"></param>
    /// <param name="voiceMethod"></param>
    /// <param name="voiceStatusCallbackMethod"></param>
    /// <param name="voiceStatusCallbackUrl"></param>
    /// <param name="voiceUrl"></param>
    /// <param name="sipRegistration"></param>
    /// <param name="domainName"></param>
    /// <param name="emergencyCallingEnabled"></param>
    /// <param name="secure"></param>
    /// <param name="byocTrunkSid"></param>
    /// <param name="emergencyCallerSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountSipSipDomain"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update the attributes of a domain
    /// </remarks>
    public Task<ApiV2010AccountSipSipDomain> UpdateSipDomain(string accountSid,
        string sid,
        string? friendlyName,
        VoiceFallbackMethod7? voiceFallbackMethod,
        string? voiceFallbackUrl,
        VoiceMethod15? voiceMethod,
        VoiceStatusCallbackMethod1? voiceStatusCallbackMethod,
        string? voiceStatusCallbackUrl,
        string? voiceUrl,
        bool? sipRegistration,
        string? domainName,
        bool? emergencyCallingEnabled,
        bool? secure,
        string? byocTrunkSid,
        string? emergencyCallerSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/Domains/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("VoiceFallbackMethod", voiceFallbackMethod),
                    new Param("VoiceFallbackUrl", voiceFallbackUrl),
                    new Param("VoiceMethod", voiceMethod),
                    new Param("VoiceStatusCallbackMethod", voiceStatusCallbackMethod),
                    new Param("VoiceStatusCallbackUrl", voiceStatusCallbackUrl),
                    new Param("VoiceUrl", voiceUrl),
                    new Param("SipRegistration", sipRegistration),
                    new Param("DomainName", domainName),
                    new Param("EmergencyCallingEnabled", emergencyCallingEnabled),
                    new Param("Secure", secure),
                    new Param("ByocTrunkSid", byocTrunkSid),
                    new Param("EmergencyCallerSid", emergencyCallerSid)]),
            JsonResponse.Create<ApiV2010AccountSipSipDomain>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
