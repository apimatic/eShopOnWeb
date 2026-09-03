using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Maxio.Core;
using Maxio.Core.Exceptions;
using Maxio.Core.Models;
using Maxio.Core.Request;
using Maxio.Core.Response;
using Maxio.Errors;
using Maxio.Models;

namespace Maxio.Api;

/// <summary>
/// Obtain an OAuth 2.0 access token for the Maxio API Gateway.
/// </summary>
public sealed class MaxioGateway
{
    private readonly RawClient _rawClient;
    private readonly Server _server;

    internal MaxioGateway(RawClient rawClient, Server server)
    {
        _rawClient = rawClient;
        _server = server;
    }

    /// <summary>
    /// Exchange client credentials for an access token
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MaxioGatewayOAuthAccessToken"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RequestAccessTokenError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Exchanges your connector's OAuth 2.0 client credentials for a bearer access token.
    /// <para>
    /// Authenticate with HTTP Basic auth (<c>client_id</c> as the username, <c>client_secret</c> as the password) or send <c>client_id</c> and <c>client_secret</c> in the form body. Then send the returned <c>access_token</c> as <c>Authorization: Bearer &lt;access_token&gt;</c> on every gateway request.
    /// </para>
    /// <para>
    /// The client-credentials grant does not issue a refresh token — when the token expires, request a new one with the same credentials.
    /// </para>
    /// <para>
    /// This endpoint is available only for connectors configured for OAuth2. It lives at your connector's root host (<c>https://{connector}.api.maxio.com/oauth/token</c>), not under the <c>/api/v1/billing</c> base path.
    /// </para>
    /// </remarks>
    public Task<MaxioGatewayOAuthAccessToken> RequestAccessToken(MaxioGatewayOAuthTokenRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Oauth("/oauth/token"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<MaxioGatewayOAuthAccessToken>(),
            RequestAccessTokenErrorResponse.Instance,
            [],
            requestOptions,
            ct);
}
