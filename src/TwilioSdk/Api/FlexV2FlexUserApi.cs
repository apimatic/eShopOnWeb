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

namespace TwilioSdk.Api;

public sealed class FlexV2FlexUserApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal FlexV2FlexUserApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Fetch flex user for the given flex user sid
    /// </summary>
    /// <param name="instanceSid">The unique ID created by Twilio to identify a Flex instance.</param>
    /// <param name="flexUserSid">The unique id for the flex user to be retrieved.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV2FlexUser"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch flex user for the given flex user sid
    /// </remarks>
    public Task<FlexV2FlexUser> FetchFlexUser(string instanceSid,
        string flexUserSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v2/Instances/{InstanceSid}/Users/{FlexUserSid}"),
            [new TemplateParam("InstanceSid", instanceSid), new TemplateParam("FlexUserSid", flexUserSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<FlexV2FlexUser>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update flex user for the given flex user sid
    /// </summary>
    /// <param name="instanceSid">The unique ID created by Twilio to identify a Flex instance.</param>
    /// <param name="flexUserSid">The unique id for the flex user.</param>
    /// <param name="email"></param>
    /// <param name="userSid"></param>
    /// <param name="locale"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV2FlexUser"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update flex user for the given flex user sid
    /// </remarks>
    public Task<FlexV2FlexUser> UpdateFlexUser(string instanceSid,
        string flexUserSid,
        string? email,
        string? userSid,
        string? locale,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v2/Instances/{InstanceSid}/Users/{FlexUserSid}"),
            [new TemplateParam("InstanceSid", instanceSid), new TemplateParam("FlexUserSid", flexUserSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Email", email),
                    new Param("UserSid", userSid),
                    new Param("Locale", locale)]),
            JsonResponse.Create<FlexV2FlexUser>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
