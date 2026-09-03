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

public sealed class FlexV1InsightsAssessmentsCommentApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal FlexV1InsightsAssessmentsCommentApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// To create a comment assessment for a conversation
    /// </summary>
    /// <param name="authorization">The Authorization HTTP request header</param>
    /// <param name="categoryId"></param>
    /// <param name="categoryName"></param>
    /// <param name="comment"></param>
    /// <param name="segmentId"></param>
    /// <param name="agentId"></param>
    /// <param name="offset"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1InsightsAssessmentsComment"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// To create a comment assessment for a conversation
    /// </remarks>
    public Task<FlexV1InsightsAssessmentsComment> CreateInsightsAssessmentsComment(string? authorization,
        string categoryId,
        string categoryName,
        string comment,
        string segmentId,
        string agentId,
        double offset,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Insights/QualityManagement/Assessments/Comments"),
            [],
            [],
            [new HeaderParam("Authorization", authorization), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("CategoryId", categoryId),
                    new Param("CategoryName", categoryName),
                    new Param("Comment", comment),
                    new Param("SegmentId", segmentId),
                    new Param("AgentId", agentId),
                    new Param("Offset", offset)]),
            JsonResponse.Create<FlexV1InsightsAssessmentsComment>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// To create a comment assessment for a conversation
    /// </summary>
    /// <param name="segmentId">The id of the segment.</param>
    /// <param name="agentId">The id of the agent.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="authorization">The Authorization HTTP request header</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListInsightsAssessmentsCommentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// To create a comment assessment for a conversation
    /// </remarks>
    public Task<ListInsightsAssessmentsCommentResponse> ListInsightsAssessmentsComment(string? segmentId,
        string? agentId,
        long? pageSize,
        int? page,
        string? pageToken,
        string? authorization,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Insights/QualityManagement/Assessments/Comments"),
            [],
            [new Param("SegmentId", segmentId),
                new Param("AgentId", agentId),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [new HeaderParam("Authorization", authorization)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListInsightsAssessmentsCommentResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
