using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Maxio.Core;
using Maxio.Core.Authentication;
using Maxio.Core.Exceptions;
using Maxio.Core.Models;
using Maxio.Core.Request;
using Maxio.Core.Response;
using Maxio.Errors;
using Maxio.Models;

namespace Maxio.Api;

public sealed class ReferralCodes
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ReferralCodes(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Validate Referral Code
    /// </summary>
    /// <param name="code">The referral code you are trying to validate</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ReferralValidationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ValidateReferralCodeError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Validates whether a referral code is valid and applicable within your site. This method is useful for validating referral codes that are entered by a customer.
    /// <para>
    /// ## Referrals Documentation
    /// </para>
    /// <para>
    /// Full documentation on how to use the referrals feature in the Advanced Billing UI can be located <see href="https://maxio.zendesk.com/hc/en-us/sections/24286965611405-Referrals">here</see>.
    /// </para>
    /// <para>
    /// ## Server Response
    /// </para>
    /// <para>
    /// If the referral code is valid the status code will be <c>200</c> and the referral code will be returned. If the referral code is invalid, a <c>404</c> response will be returned.
    /// </para>
    /// </remarks>
    public Task<ReferralValidationResponse> ValidateReferralCode(string code,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/referral_codes/validate.json"),
            [],
            [new Param("code", code)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ReferralValidationResponse>(),
            ValidateReferralCodeErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
