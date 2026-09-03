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

namespace Twilio.Api;

public sealed class FlexV1InsightsQuestionnairesQuestionApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal FlexV1InsightsQuestionnairesQuestionApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// To create a question for a Category
    /// </summary>
    /// <param name="authorization">The Authorization HTTP request header</param>
    /// <param name="categorySid"></param>
    /// <param name="question"></param>
    /// <param name="answerSetId"></param>
    /// <param name="allowNa"></param>
    /// <param name="description"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1InsightsQuestionnairesQuestion"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// To create a question for a Category
    /// </remarks>
    public Task<FlexV1InsightsQuestionnairesQuestion> CreateInsightsQuestionnairesQuestion(string? authorization,
        string categorySid,
        string question,
        string answerSetId,
        bool allowNa,
        string? description,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Insights/QualityManagement/Questions"),
            [],
            [],
            [new HeaderParam("Authorization", authorization), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("CategorySid", categorySid),
                    new Param("Question", question),
                    new Param("AnswerSetId", answerSetId),
                    new Param("AllowNa", allowNa),
                    new Param("Description", description)]),
            JsonResponse.Create<FlexV1InsightsQuestionnairesQuestion>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task DeleteInsightsQuestionnairesQuestion(string questionSid,
        string? authorization,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Insights/QualityManagement/Questions/{QuestionSid}"),
            [new TemplateParam("QuestionSid", questionSid)],
            [],
            [new HeaderParam("Authorization", authorization), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// To get all the question for the given categories
    /// </summary>
    /// <param name="categorySid">The list of category SIDs</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="authorization">The Authorization HTTP request header</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListInsightsQuestionnairesQuestionResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// To get all the question for the given categories
    /// </remarks>
    public Task<ListInsightsQuestionnairesQuestionResponse> ListInsightsQuestionnairesQuestion(IReadOnlyList<string>? categorySid,
        long? pageSize,
        int? page,
        string? pageToken,
        string? authorization,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Insights/QualityManagement/Questions"),
            [],
            [new Param("CategorySid", categorySid),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [new HeaderParam("Authorization", authorization)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListInsightsQuestionnairesQuestionResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// To update the question
    /// </summary>
    /// <param name="questionSid">The SID of the question</param>
    /// <param name="authorization">The Authorization HTTP request header</param>
    /// <param name="allowNa"></param>
    /// <param name="categorySid"></param>
    /// <param name="question"></param>
    /// <param name="description"></param>
    /// <param name="answerSetId"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1InsightsQuestionnairesQuestion"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// To update the question
    /// </remarks>
    public Task<FlexV1InsightsQuestionnairesQuestion> UpdateInsightsQuestionnairesQuestion(string questionSid,
        string? authorization,
        bool allowNa,
        string? categorySid,
        string? question,
        string? description,
        string? answerSetId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Insights/QualityManagement/Questions/{QuestionSid}"),
            [new TemplateParam("QuestionSid", questionSid)],
            [],
            [new HeaderParam("Authorization", authorization), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("AllowNa", allowNa),
                    new Param("CategorySid", categorySid),
                    new Param("Question", question),
                    new Param("Description", description),
                    new Param("AnswerSetId", answerSetId)]),
            JsonResponse.Create<FlexV1InsightsQuestionnairesQuestion>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
