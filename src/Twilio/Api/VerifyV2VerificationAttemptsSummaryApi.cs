using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Exceptions;
using Twilio.Core.Extensions;
using Twilio.Core.Models;
using Twilio.Core.Request;
using Twilio.Core.Response;
using Twilio.Models;
using Twilio.Models.Enums;

namespace Twilio.Api;

public sealed class VerifyV2VerificationAttemptsSummaryApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VerifyV2VerificationAttemptsSummaryApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Get a summary of how many attempts were made and how many were converted.
    /// </summary>
    /// <param name="verifyServiceSid">Filter used to consider only Verification Attempts of the given verify service on the summary aggregation.</param>
    /// <param name="dateCreatedAfter">Datetime filter used to consider only Verification Attempts created after this datetime on the summary aggregation. Given as GMT in ISO 8601 formatted datetime string: yyyy-MM-dd'T'HH:mm:ss'Z.</param>
    /// <param name="dateCreatedBefore">Datetime filter used to consider only Verification Attempts created before this datetime on the summary aggregation. Given as GMT in ISO 8601 formatted datetime string: yyyy-MM-dd'T'HH:mm:ss'Z.</param>
    /// <param name="country">Filter used to consider only Verification Attempts sent to the specified destination country on the summary aggregation.</param>
    /// <param name="channel">Filter Verification Attempts considered on the summary aggregation by communication channel.</param>
    /// <param name="destinationPrefix">Filter the Verification Attempts considered on the summary aggregation by Destination prefix. It is the prefix of a phone number in E.164 format.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2VerificationAttemptsSummary"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Get a summary of how many attempts were made and how many were converted.
    /// </remarks>
    public Task<VerifyV2VerificationAttemptsSummary> FetchVerificationAttemptsSummary(string? verifyServiceSid,
        DateTimeOffset? dateCreatedAfter,
        DateTimeOffset? dateCreatedBefore,
        string? country,
        VerificationAttemptsSummaryEnumChannels? channel,
        string? destinationPrefix,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Attempts/Summary"),
            [],
            [new Param("VerifyServiceSid", verifyServiceSid),
                new Param("DateCreatedAfter", dateCreatedAfter?.ToIso8601()),
                new Param("DateCreatedBefore", dateCreatedBefore?.ToIso8601()),
                new Param("Country", country),
                new Param("Channel", channel),
                new Param("DestinationPrefix", destinationPrefix)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VerifyV2VerificationAttemptsSummary>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
