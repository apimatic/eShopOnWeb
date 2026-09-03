using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Exceptions;
using Twilio.Core.Extensions;
using Twilio.Core.Models;
using Twilio.Core.Request;
using Twilio.Core.Response;
using Twilio.Models;

namespace Twilio.Api;

public sealed class Api20100401LastMonth
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401LastMonth(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Usage records for last month
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the UsageRecord resources to read.</param>
    /// <param name="category">The <see href="https://www.twilio.com/docs/usage/api/usage-record#usage-categories">usage category</see> of the UsageRecord resources to read. Only UsageRecord resources in the specified category are retrieved.</param>
    /// <param name="startDate">Only include usage that has occurred on or after this date. Specify the date in GMT and format as <c>YYYY-MM-DD</c>. You can also specify offsets from the current date, such as: <c>-30days</c>, which will set the start date to be 30 days before the current date.</param>
    /// <param name="endDate">Only include usage that occurred on or before this date. Specify the date in GMT and format as <c>YYYY-MM-DD</c>.  You can also specify offsets from the current date, such as: <c>+30days</c>, which will set the end date to 30 days from the current date.</param>
    /// <param name="includeSubaccounts">Whether to include usage from the master account and all its subaccounts. Can be: <c>true</c> (the default) to include usage from the master account and all subaccounts or <c>false</c> to retrieve usage from only the specified account.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListUsageRecordLastMonthResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListUsageRecordLastMonthResponse> ListUsageRecordLastMonth(string accountSid,
        string? category,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        bool? includeSubaccounts,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Usage/Records/LastMonth.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [new Param("Category", category),
                new Param("StartDate", startDate?.ToDate()),
                new Param("EndDate", endDate?.ToDate()),
                new Param("IncludeSubaccounts", includeSubaccounts),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListUsageRecordLastMonthResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
