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

public sealed class NumbersV2Regulation
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal NumbersV2Regulation(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Fetch specific Regulation Instance.
    /// </summary>
    /// <param name="sid">The unique string that identifies the Regulation resource.</param>
    /// <param name="includeConstraints">A boolean parameter indicating whether to include constraints or not for supporting end user, documents and their fields</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV2RegulatoryComplianceRegulation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch specific Regulation Instance.
    /// </remarks>
    public Task<NumbersV2RegulatoryComplianceRegulation> FetchRegulation(string sid,
        bool? includeConstraints,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/RegulatoryCompliance/Regulations/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [new Param("IncludeConstraints", includeConstraints)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<NumbersV2RegulatoryComplianceRegulation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Regulations.
    /// </summary>
    /// <param name="endUserType">The type of End User the regulation requires - can be <c>individual</c> or <c>business</c>.</param>
    /// <param name="isoCountry">The ISO country code of the phone number's country.</param>
    /// <param name="numberType">The type of phone number that the regulatory requiremnt is restricting.</param>
    /// <param name="includeConstraints">A boolean parameter indicating whether to include constraints or not for supporting end user, documents and their fields</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListRegulationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Regulations.
    /// </remarks>
    public Task<ListRegulationResponse> ListRegulation(RegulationEnumEndUserType? endUserType,
        string? isoCountry,
        string? numberType,
        bool? includeConstraints,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/RegulatoryCompliance/Regulations"),
            [],
            [new Param("EndUserType", endUserType),
                new Param("IsoCountry", isoCountry),
                new Param("NumberType", numberType),
                new Param("IncludeConstraints", includeConstraints),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListRegulationResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
