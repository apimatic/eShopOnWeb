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

public sealed class FlexV1InsightsQuestionnairesApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal FlexV1InsightsQuestionnairesApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// To create a Questionnaire
    /// </summary>
    /// <param name="authorization">The Authorization HTTP request header</param>
    /// <param name="name"></param>
    /// <param name="description"></param>
    /// <param name="active"></param>
    /// <param name="questionSids"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1InsightsQuestionnaires"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// To create a Questionnaire
    /// </remarks>
    public Task<FlexV1InsightsQuestionnaires> CreateInsightsQuestionnaires(string? authorization,
        string name,
        string? description,
        bool? active,
        IReadOnlyList<string>? questionSids,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Insights/QualityManagement/Questionnaires"),
            [],
            [],
            [new HeaderParam("Authorization", authorization), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Name", name),
                    new Param("Description", description),
                    new Param("Active", active),
                    new Param("QuestionSids", questionSids)]),
            JsonResponse.Create<FlexV1InsightsQuestionnaires>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// To delete the questionnaire
    /// </summary>
    /// <param name="questionnaireSid">The SID of the questionnaire</param>
    /// <param name="authorization">The Authorization HTTP request header</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// To delete the questionnaire
    /// </remarks>
    public Task DeleteInsightsQuestionnaires(string questionnaireSid,
        string? authorization,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Insights/QualityManagement/Questionnaires/{QuestionnaireSid}"),
            [new TemplateParam("QuestionnaireSid", questionnaireSid)],
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
    /// To get the Questionnaire Detail
    /// </summary>
    /// <param name="questionnaireSid">The SID of the questionnaire</param>
    /// <param name="authorization">The Authorization HTTP request header</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1InsightsQuestionnaires"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// To get the Questionnaire Detail
    /// </remarks>
    public Task<FlexV1InsightsQuestionnaires> FetchInsightsQuestionnaires(string questionnaireSid,
        string? authorization,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Insights/QualityManagement/Questionnaires/{QuestionnaireSid}"),
            [new TemplateParam("QuestionnaireSid", questionnaireSid)],
            [],
            [new HeaderParam("Authorization", authorization)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<FlexV1InsightsQuestionnaires>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// To get all questionnaires with questions
    /// </summary>
    /// <param name="includeInactive">Flag indicating whether to include inactive questionnaires or not</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="authorization">The Authorization HTTP request header</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListInsightsQuestionnairesResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// To get all questionnaires with questions
    /// </remarks>
    public Task<ListInsightsQuestionnairesResponse> ListInsightsQuestionnaires(bool? includeInactive,
        long? pageSize,
        int? page,
        string? pageToken,
        string? authorization,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Insights/QualityManagement/Questionnaires"),
            [],
            [new Param("IncludeInactive", includeInactive),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [new HeaderParam("Authorization", authorization)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListInsightsQuestionnairesResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// To update the questionnaire
    /// </summary>
    /// <param name="questionnaireSid">The SID of the questionnaire</param>
    /// <param name="authorization">The Authorization HTTP request header</param>
    /// <param name="active"></param>
    /// <param name="name"></param>
    /// <param name="description"></param>
    /// <param name="questionSids"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1InsightsQuestionnaires"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// To update the questionnaire
    /// </remarks>
    public Task<FlexV1InsightsQuestionnaires> UpdateInsightsQuestionnaires(string questionnaireSid,
        string? authorization,
        bool active,
        string? name,
        string? description,
        IReadOnlyList<string>? questionSids,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Insights/QualityManagement/Questionnaires/{QuestionnaireSid}"),
            [new TemplateParam("QuestionnaireSid", questionnaireSid)],
            [],
            [new HeaderParam("Authorization", authorization), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Active", active),
                    new Param("Name", name),
                    new Param("Description", description),
                    new Param("QuestionSids", questionSids)]),
            JsonResponse.Create<FlexV1InsightsQuestionnaires>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
