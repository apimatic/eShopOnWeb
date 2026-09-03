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

public sealed class VerifyV2MessagingConfiguration
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VerifyV2MessagingConfiguration(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new MessagingConfiguration for a service.
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/verify/api/service">Service</see> that the resource is associated with.</param>
    /// <param name="country"></param>
    /// <param name="messagingServiceSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2ServiceMessagingConfiguration"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new MessagingConfiguration for a service.
    /// </remarks>
    public Task<VerifyV2ServiceMessagingConfiguration> CreateMessagingConfiguration(string serviceSid,
        string country,
        string messagingServiceSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/MessagingConfigurations"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Country", country),
                    new Param("MessagingServiceSid", messagingServiceSid)]),
            JsonResponse.Create<VerifyV2ServiceMessagingConfiguration>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a specific MessagingConfiguration.
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/verify/api/service">Service</see> that the resource is associated with.</param>
    /// <param name="country">The <see href="https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2">ISO-3166-1</see> country code of the country this configuration will be applied to. If this is a global configuration, Country will take the value <c>all</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a specific MessagingConfiguration.
    /// </remarks>
    public Task DeleteMessagingConfiguration(string serviceSid,
        string country,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/MessagingConfigurations/{Country}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Country", country)],
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
    /// Fetch a specific MessagingConfiguration.
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/verify/api/service">Service</see> that the resource is associated with.</param>
    /// <param name="country">The <see href="https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2">ISO-3166-1</see> country code of the country this configuration will be applied to. If this is a global configuration, Country will take the value <c>all</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2ServiceMessagingConfiguration"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific MessagingConfiguration.
    /// </remarks>
    public Task<VerifyV2ServiceMessagingConfiguration> FetchMessagingConfiguration(string serviceSid,
        string country,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/MessagingConfigurations/{Country}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Country", country)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VerifyV2ServiceMessagingConfiguration>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Messaging Configurations for a Service.
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/verify/api/service">Service</see> that the resource is associated with.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListMessagingConfigurationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Messaging Configurations for a Service.
    /// </remarks>
    public Task<ListMessagingConfigurationResponse> ListMessagingConfiguration(string serviceSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/MessagingConfigurations"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListMessagingConfigurationResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update a specific MessagingConfiguration
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/verify/api/service">Service</see> that the resource is associated with.</param>
    /// <param name="country">The <see href="https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2">ISO-3166-1</see> country code of the country this configuration will be applied to. If this is a global configuration, Country will take the value <c>all</c>.</param>
    /// <param name="messagingServiceSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2ServiceMessagingConfiguration"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update a specific MessagingConfiguration
    /// </remarks>
    public Task<VerifyV2ServiceMessagingConfiguration> UpdateMessagingConfiguration(string serviceSid,
        string country,
        string messagingServiceSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/MessagingConfigurations/{Country}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Country", country)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("MessagingServiceSid", messagingServiceSid)]),
            JsonResponse.Create<VerifyV2ServiceMessagingConfiguration>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
