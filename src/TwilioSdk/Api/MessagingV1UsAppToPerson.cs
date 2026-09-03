using System;
using System.Collections.Generic;
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
using TwilioSdk.Models.AnyOf;

namespace TwilioSdk.Api;

public sealed class MessagingV1UsAppToPerson
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal MessagingV1UsAppToPerson(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// A service for (fetch/create/delete) A2P Campaign for a Messaging Service
    /// </summary>
    /// <param name="messagingServiceSid">The SID of the <see href="https://www.twilio.com/docs/messaging/api/service-resource">Messaging Service</see> to create the resources from.</param>
    /// <param name="xTwilioApiVersion">The version of the Messaging API to use for this request</param>
    /// <param name="brandRegistrationSid"></param>
    /// <param name="description"></param>
    /// <param name="messageFlow"></param>
    /// <param name="messageSamples"></param>
    /// <param name="usAppToPersonUsecase"></param>
    /// <param name="hasEmbeddedLinks"></param>
    /// <param name="hasEmbeddedPhone"></param>
    /// <param name="optInMessage"></param>
    /// <param name="optOutMessage"></param>
    /// <param name="helpMessage"></param>
    /// <param name="optInKeywords"></param>
    /// <param name="optOutKeywords"></param>
    /// <param name="helpKeywords"></param>
    /// <param name="subscriberOptIn"></param>
    /// <param name="ageGated"></param>
    /// <param name="directLending"></param>
    /// <param name="privacyPolicyUrl"></param>
    /// <param name="termsAndConditionsUrl"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MessagingV1ServiceUsAppToPersonResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<MessagingV1ServiceUsAppToPersonResponse> CreateUsAppToPerson(string messagingServiceSid,
        string? xTwilioApiVersion,
        string brandRegistrationSid,
        string description,
        string messageFlow,
        IReadOnlyList<string> messageSamples,
        string usAppToPersonUsecase,
        bool hasEmbeddedLinks,
        bool hasEmbeddedPhone,
        string? optInMessage,
        string? optOutMessage,
        string? helpMessage,
        IReadOnlyList<string>? optInKeywords,
        IReadOnlyList<string>? optOutKeywords,
        IReadOnlyList<string>? helpKeywords,
        bool? subscriberOptIn,
        bool? ageGated,
        bool? directLending,
        string? privacyPolicyUrl,
        string? termsAndConditionsUrl,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services/{MessagingServiceSid}/Compliance/Usa2p"),
            [new TemplateParam("MessagingServiceSid", messagingServiceSid)],
            [],
            [new HeaderParam("X-Twilio-Api-Version", xTwilioApiVersion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("BrandRegistrationSid", brandRegistrationSid),
                    new Param("Description", description),
                    new Param("MessageFlow", messageFlow),
                    new Param("MessageSamples", messageSamples),
                    new Param("UsAppToPersonUsecase", usAppToPersonUsecase),
                    new Param("HasEmbeddedLinks", hasEmbeddedLinks),
                    new Param("HasEmbeddedPhone", hasEmbeddedPhone),
                    new Param("OptInMessage", optInMessage),
                    new Param("OptOutMessage", optOutMessage),
                    new Param("HelpMessage", helpMessage),
                    new Param("OptInKeywords", optInKeywords),
                    new Param("OptOutKeywords", optOutKeywords),
                    new Param("HelpKeywords", helpKeywords),
                    new Param("SubscriberOptIn", subscriberOptIn),
                    new Param("AgeGated", ageGated),
                    new Param("DirectLending", directLending),
                    new Param("PrivacyPolicyUrl", privacyPolicyUrl),
                    new Param("TermsAndConditionsUrl", termsAndConditionsUrl)]),
            JsonResponse.Create<MessagingV1ServiceUsAppToPersonResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// A service for (fetch/create/delete) A2P Campaign for a Messaging Service
    /// </summary>
    /// <param name="messagingServiceSid">The SID of the <see href="https://www.twilio.com/docs/messaging/api/service-resource">Messaging Service</see> to delete the resource from.</param>
    /// <param name="sid">The SID of the US A2P Compliance resource to delete <c>QE2c6890da8086d771620e9b13fadeba0b</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task DeleteUsAppToPerson(string messagingServiceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services/{MessagingServiceSid}/Compliance/Usa2p/{Sid}"),
            [new TemplateParam("MessagingServiceSid", messagingServiceSid), new TemplateParam("Sid", sid)],
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
    /// A service for (fetch/create/delete) A2P Campaign for a Messaging Service
    /// </summary>
    /// <param name="messagingServiceSid">The SID of the <see href="https://www.twilio.com/docs/messaging/api/service-resource">Messaging Service</see> to fetch the resource from.</param>
    /// <param name="sid">The SID of the US A2P Compliance resource to fetch <c>QE2c6890da8086d771620e9b13fadeba0b</c>.</param>
    /// <param name="xTwilioApiVersion">The version of the Messaging API to use for this request</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MessagingV1ServiceUsAppToPersonResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<MessagingV1ServiceUsAppToPersonResponse> FetchUsAppToPerson(string messagingServiceSid,
        string sid,
        string? xTwilioApiVersion,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services/{MessagingServiceSid}/Compliance/Usa2p/{Sid}"),
            [new TemplateParam("MessagingServiceSid", messagingServiceSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("X-Twilio-Api-Version", xTwilioApiVersion)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<MessagingV1ServiceUsAppToPersonResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// A service for (fetch/create/delete) A2P Campaign for a Messaging Service
    /// </summary>
    /// <param name="messagingServiceSid">The SID of the <see href="https://www.twilio.com/docs/messaging/api/service-resource">Messaging Service</see> to fetch the resource from.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="xTwilioApiVersion">The version of the Messaging API to use for this request</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListUsAppToPersonResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListUsAppToPersonResponse> ListUsAppToPerson(string messagingServiceSid,
        long? pageSize,
        int? page,
        string? pageToken,
        string? xTwilioApiVersion,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services/{MessagingServiceSid}/Compliance/Usa2p"),
            [new TemplateParam("MessagingServiceSid", messagingServiceSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [new HeaderParam("X-Twilio-Api-Version", xTwilioApiVersion)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListUsAppToPersonResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// A service for (fetch/create/delete) A2P Campaign for a Messaging Service
    /// </summary>
    /// <param name="messagingServiceSid">The SID of the <see href="https://www.twilio.com/docs/messaging/services/api">Messaging Service</see> to update the resource from.</param>
    /// <param name="sid">The SID of the US A2P Compliance resource to update <c>QE2c6890da8086d771620e9b13fadeba0b</c>.</param>
    /// <param name="xTwilioApiVersion">The version of the Messaging API to use for this request</param>
    /// <param name="hasEmbeddedLinks"></param>
    /// <param name="hasEmbeddedPhone"></param>
    /// <param name="messageSamples"></param>
    /// <param name="messageFlow"></param>
    /// <param name="description"></param>
    /// <param name="ageGated"></param>
    /// <param name="directLending"></param>
    /// <param name="privacyPolicyUrl"></param>
    /// <param name="termsAndConditionsUrl"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MessagingV1ServiceUsAppToPersonResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<MessagingV1ServiceUsAppToPersonResponse> UpdateUsAppToPerson(string messagingServiceSid,
        string sid,
        string? xTwilioApiVersion,
        bool hasEmbeddedLinks,
        bool hasEmbeddedPhone,
        IReadOnlyList<string> messageSamples,
        string messageFlow,
        string description,
        bool ageGated,
        bool directLending,
        string? privacyPolicyUrl,
        string? termsAndConditionsUrl,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services/{MessagingServiceSid}/Compliance/Usa2p/{Sid}"),
            [new TemplateParam("MessagingServiceSid", messagingServiceSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("X-Twilio-Api-Version", xTwilioApiVersion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("HasEmbeddedLinks", hasEmbeddedLinks),
                    new Param("HasEmbeddedPhone", hasEmbeddedPhone),
                    new Param("MessageSamples", messageSamples),
                    new Param("MessageFlow", messageFlow),
                    new Param("Description", description),
                    new Param("AgeGated", ageGated),
                    new Param("DirectLending", directLending),
                    new Param("PrivacyPolicyUrl", privacyPolicyUrl),
                    new Param("TermsAndConditionsUrl", termsAndConditionsUrl)]),
            JsonResponse.Create<MessagingV1ServiceUsAppToPersonResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
