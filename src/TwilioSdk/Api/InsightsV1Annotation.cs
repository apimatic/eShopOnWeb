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

public sealed class InsightsV1Annotation
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal InsightsV1Annotation(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Get the Annotation for a specific Call.
    /// </summary>
    /// <param name="callSid">The unique SID identifier of the Call.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="InsightsV1CallAnnotation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Get the Annotation for a specific Call.
    /// </remarks>
    public Task<InsightsV1CallAnnotation> FetchAnnotation(string callSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default14("/v1/Voice/{CallSid}/Annotation"),
            [new TemplateParam("CallSid", callSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<InsightsV1CallAnnotation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an Annotation for a specific Call.
    /// </summary>
    /// <param name="callSid">The unique string that Twilio created to identify this Call resource. It always starts with a CA.</param>
    /// <param name="answeredBy"></param>
    /// <param name="connectivityIssue"></param>
    /// <param name="qualityIssues"></param>
    /// <param name="spam"></param>
    /// <param name="callScore"></param>
    /// <param name="comment"></param>
    /// <param name="incident"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="InsightsV1CallAnnotation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an Annotation for a specific Call.
    /// </remarks>
    public Task<InsightsV1CallAnnotation> UpdateAnnotation(string callSid,
        AnnotationEnumAnsweredBy? answeredBy,
        AnnotationEnumConnectivityIssue? connectivityIssue,
        string? qualityIssues,
        bool? spam,
        int? callScore,
        string? comment,
        string? incident,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default14("/v1/Voice/{CallSid}/Annotation"),
            [new TemplateParam("CallSid", callSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("AnsweredBy", answeredBy),
                    new Param("ConnectivityIssue", connectivityIssue),
                    new Param("QualityIssues", qualityIssues),
                    new Param("Spam", spam),
                    new Param("CallScore", callScore),
                    new Param("Comment", comment),
                    new Param("Incident", incident)]),
            JsonResponse.Create<InsightsV1CallAnnotation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
