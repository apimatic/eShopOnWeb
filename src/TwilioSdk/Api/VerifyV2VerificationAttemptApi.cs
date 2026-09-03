using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Core.Extensions;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Request;
using TwilioSdk.Core.Response;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Api;

public sealed class VerifyV2VerificationAttemptApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VerifyV2VerificationAttemptApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Fetch a specific verification attempt.
    /// </summary>
    /// <param name="sid">The unique SID identifier of a Verification Attempt</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2VerificationAttempt"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific verification attempt.
    /// </remarks>
    public Task<VerifyV2VerificationAttempt> FetchVerificationAttempt(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Attempts/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VerifyV2VerificationAttempt>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// List all the verification attempts for a given Account.
    /// </summary>
    /// <param name="dateCreatedAfter">Datetime filter used to consider only Verification Attempts created after this datetime on the summary aggregation. Given as GMT in ISO 8601 formatted datetime string: yyyy-MM-dd'T'HH:mm:ss'Z.</param>
    /// <param name="dateCreatedBefore">Datetime filter used to consider only Verification Attempts created before this datetime on the summary aggregation. Given as GMT in ISO 8601 formatted datetime string: yyyy-MM-dd'T'HH:mm:ss'Z.</param>
    /// <param name="channelDataTo">Destination of a verification. It is phone number in E.164 format.</param>
    /// <param name="country">Filter used to query Verification Attempts sent to the specified destination country.</param>
    /// <param name="channel">Filter used to query Verification Attempts by communication channel.</param>
    /// <param name="verifyServiceSid">Filter used to query Verification Attempts by verify service. Only attempts of the provided SID will be returned.</param>
    /// <param name="verificationSid">Filter used to return all the Verification Attempts of a single verification. Only attempts of the provided verification SID will be returned.</param>
    /// <param name="status">Filter used to query Verification Attempts by conversion status. Valid values are <c>UNCONVERTED</c>, for attempts that were not converted, and <c>CONVERTED</c>, for attempts that were confirmed.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListVerificationAttemptResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// List all the verification attempts for a given Account.
    /// </remarks>
    public Task<ListVerificationAttemptResponse> ListVerificationAttempt(DateTimeOffset? dateCreatedAfter,
        DateTimeOffset? dateCreatedBefore,
        string? channelDataTo,
        string? country,
        VerificationAttemptEnumChannels? channel,
        string? verifyServiceSid,
        string? verificationSid,
        VerificationAttemptEnumConversionStatus? status,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Attempts"),
            [],
            [new Param("DateCreatedAfter", dateCreatedAfter?.ToIso8601()),
                new Param("DateCreatedBefore", dateCreatedBefore?.ToIso8601()),
                new Param("ChannelData.To", channelDataTo),
                new Param("Country", country),
                new Param("Channel", channel),
                new Param("VerifyServiceSid", verifyServiceSid),
                new Param("VerificationSid", verificationSid),
                new Param("Status", status),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListVerificationAttemptResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
