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

public sealed class LookupsV1PhoneNumberApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal LookupsV1PhoneNumberApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Detailed information on phone numbers
    /// </summary>
    /// <param name="phoneNumber">The phone number to lookup in <see href="https://www.twilio.com/docs/glossary/what-e164">E.164</see> format, which consists of a + followed by the country code and subscriber number.</param>
    /// <param name="countryCode">The <see href="https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2">ISO country code</see> of the phone number to fetch. This is used to specify the country when the phone number is provided in a national format.</param>
    /// <param name="type">The type of information to return. Can be: <c>carrier</c> or <c>caller-name</c>. The default is null. To retrieve both types of information, specify this parameter twice; once with <c>carrier</c> and once with <c>caller-name</c> as the value.</param>
    /// <param name="addOns">The <c>unique_name</c> of an Add-on you would like to invoke. Can be the <c>unique_name</c> of an Add-on that is installed on your account. You can specify multiple instances of this parameter to invoke multiple Add-ons. For more information about  Add-ons, see the <see href="https://www.twilio.com/docs/add-ons">Add-ons documentation</see>.</param>
    /// <param name="addOnsData">Data specific to the add-on you would like to invoke. The content and format of this value depends on the add-on.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="LookupsV1PhoneNumber"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<LookupsV1PhoneNumber> FetchPhoneNumber2(string phoneNumber,
        string? countryCode,
        IReadOnlyList<string>? type,
        IReadOnlyList<string>? addOns,
        object? addOnsData,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default4("/v1/PhoneNumbers/{PhoneNumber}"),
            [new TemplateParam("PhoneNumber", phoneNumber)],
            [new Param("CountryCode", countryCode),
                new Param("Type", type),
                new Param("AddOns", addOns),
                new Param("AddOnsData", addOnsData)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<LookupsV1PhoneNumber>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
