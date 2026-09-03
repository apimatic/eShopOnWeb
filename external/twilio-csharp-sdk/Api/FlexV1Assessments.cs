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

public sealed class FlexV1Assessments
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal FlexV1Assessments(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Add assessments against conversation to dynamo db. Used in assessments screen by user. Users can select the questionnaire and pick up answers for each and every question.
    /// </summary>
    /// <param name="authorization">The Authorization HTTP request header</param>
    /// <param name="categorySid"></param>
    /// <param name="categoryName"></param>
    /// <param name="segmentId"></param>
    /// <param name="agentId"></param>
    /// <param name="offset"></param>
    /// <param name="metricId"></param>
    /// <param name="metricName"></param>
    /// <param name="answerText"></param>
    /// <param name="answerId"></param>
    /// <param name="questionnaireSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1InsightsAssessments"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Add assessments against conversation to dynamo db. Used in assessments screen by user. Users can select the questionnaire and pick up answers for each and every question.
    /// </remarks>
    public Task<FlexV1InsightsAssessments> CreateInsightsAssessments(string? authorization,
        string categorySid,
        string categoryName,
        string segmentId,
        string agentId,
        double offset,
        string metricId,
        string metricName,
        string answerText,
        string answerId,
        string questionnaireSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Insights/QualityManagement/Assessments"),
            [],
            [],
            [new HeaderParam("Authorization", authorization), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("CategorySid", categorySid),
                    new Param("CategoryName", categoryName),
                    new Param("SegmentId", segmentId),
                    new Param("AgentId", agentId),
                    new Param("Offset", offset),
                    new Param("MetricId", metricId),
                    new Param("MetricName", metricName),
                    new Param("AnswerText", answerText),
                    new Param("AnswerId", answerId),
                    new Param("QuestionnaireSid", questionnaireSid)]),
            JsonResponse.Create<FlexV1InsightsAssessments>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Get assessments done for a conversation by logged in user
    /// </summary>
    /// <param name="segmentId">The id of the segment.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="authorization">The Authorization HTTP request header</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListInsightsAssessmentsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Get assessments done for a conversation by logged in user
    /// </remarks>
    public Task<ListInsightsAssessmentsResponse> ListInsightsAssessments(string? segmentId,
        long? pageSize,
        int? page,
        string? pageToken,
        string? authorization,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Insights/QualityManagement/Assessments"),
            [],
            [new Param("SegmentId", segmentId),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [new HeaderParam("Authorization", authorization)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListInsightsAssessmentsResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update a specific Assessment assessed earlier
    /// </summary>
    /// <param name="assessmentSid">The SID of the assessment to be modified</param>
    /// <param name="authorization">The Authorization HTTP request header</param>
    /// <param name="offset"></param>
    /// <param name="answerText"></param>
    /// <param name="answerId"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1InsightsAssessments"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update a specific Assessment assessed earlier
    /// </remarks>
    public Task<FlexV1InsightsAssessments> UpdateInsightsAssessments(string assessmentSid,
        string? authorization,
        double offset,
        string answerText,
        string answerId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Insights/QualityManagement/Assessments/{AssessmentSid}"),
            [new TemplateParam("AssessmentSid", assessmentSid)],
            [],
            [new HeaderParam("Authorization", authorization), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Offset", offset),
                    new Param("AnswerText", answerText),
                    new Param("AnswerId", answerId)]),
            JsonResponse.Create<FlexV1InsightsAssessments>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
