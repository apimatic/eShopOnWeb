using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Services.Twilio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TwilioSdk;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Twilio;

public class TwilioSmsProviderTests
{
    private static readonly TwilioOptions TestOptions = new()
    {
        AccountSid = "ACtest0000000000000000000000000000",
        AuthToken = "test-auth-token",
        FromNumber = "+15550001111",
        MessagingServiceSid = "MGtest0000000000000000000000000000"
    };

    private static TwilioSmsProvider ProviderReturning(Func<HttpRequestMessage, int, HttpResponseMessage> responder, out StubHandler handler)
    {
        handler = new StubHandler(responder);
        var client = new TwilioSdkClient(new HttpClient(handler), new TwilioSdkClientOptions());
        return new TwilioSmsProvider(client, Options.Create(TestOptions), NullLogger<TwilioSmsProvider>.Instance);
    }

    private static TwilioSmsProvider ProviderReturning(Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHandler handler)
        => ProviderReturning((request, _) => responder(request), out handler);

    [Fact]
    public async Task ValidatePhoneNumber_Valid_ReturnsCanonicalForm()
    {
        var provider = ProviderReturning(_ => StubHandler.Json(HttpStatusCode.OK, """
            { "valid": true, "phone_number": "+14155552671", "national_format": "(415) 555-2671",
              "validation_errors": [], "country_code": "US", "calling_country_code": "1" }
            """), out var handler);

        var result = await provider.ValidatePhoneNumberAsync("4155552671");

        Assert.True(result.IsValid);
        Assert.Equal("+14155552671", result.CanonicalNumber);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Contains("lookups.twilio.com", handler.LastRequest.RequestUri!.AbsoluteUri);
        Assert.Contains("/v2/PhoneNumbers/", handler.LastRequest.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task ValidatePhoneNumber_Invalid_ReportsErrors()
    {
        var provider = ProviderReturning(_ => StubHandler.Json(HttpStatusCode.OK, """
            { "valid": false, "phone_number": null, "validation_errors": ["TOO_SHORT"] }
            """), out _);

        var result = await provider.ValidatePhoneNumberAsync("123");

        Assert.False(result.IsValid);
        Assert.Null(result.CanonicalNumber);
        Assert.Contains("TOO_SHORT", result.ValidationErrors);
    }

    [Fact]
    public async Task SendAsync_SendsFormBodyAndMapsResult()
    {
        var provider = ProviderReturning(_ => StubHandler.Json(HttpStatusCode.Created, """
            { "sid": "SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "status": "queued", "to": "+14155552671",
              "from": "+15550001111", "body": "hello", "error_code": null, "error_message": null,
              "date_created": null, "date_sent": null, "date_updated": null }
            """), out var handler);

        var result = await provider.SendAsync("+14155552671", "hello");

        Assert.Equal("SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.ProviderMessageSid);
        Assert.Equal("queued", result.ProviderStatus);

        var request = handler.LastRequest!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("api.twilio.com", request.RequestUri!.AbsoluteUri);
        Assert.EndsWith("/Messages.json", request.RequestUri.AbsolutePath);
        var sentBody = handler.LastRequestBody;
        Assert.Contains("To=%2B14155552671", sentBody);
        Assert.Contains("From=%2B15550001111", sentBody);
        Assert.Contains("Body=hello", sentBody);
    }

    [Fact]
    public async Task SendAsync_ApiRejection_ThrowsWithProviderStatus()
    {
        var provider = ProviderReturning(_ => StubHandler.Json(HttpStatusCode.BadRequest,
            """{ "code": 21211, "message": "The 'To' number is not a valid phone number.", "status": 400 }"""), out _);

        var ex = await Assert.ThrowsAsync<SmsProviderException>(() => provider.SendAsync("not-a-number", "hello"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("21211", ex.Message);
    }

    [Fact]
    public async Task SendAsync_TransportFailure_ThrowsWithoutStatus()
    {
        var provider = ProviderReturning(_ => throw new HttpRequestException("connection reset"), out _);

        var ex = await Assert.ThrowsAsync<SmsProviderException>(() => provider.SendAsync("+14155552671", "hello"));

        Assert.Null(ex.StatusCode);
    }

    [Fact]
    public async Task ScheduleAsync_UsesMessagingServiceAndFixedSchedule()
    {
        var provider = ProviderReturning(_ => StubHandler.Json(HttpStatusCode.Created, """
            { "sid": "SMbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "status": "scheduled", "to": "+14155552671",
              "from": null, "messaging_service_sid": "MGtest0000000000000000000000000000", "body": "later",
              "error_code": null, "error_message": null, "date_created": null, "date_sent": null, "date_updated": null }
            """), out var handler);

        var sendAt = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var result = await provider.ScheduleAsync("+14155552671", "later", sendAt);

        Assert.Equal("SMbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", result.ProviderMessageSid);
        Assert.Equal("scheduled", result.ProviderStatus);

        var sentBody = handler.LastRequestBody;
        Assert.Contains("MessagingServiceSid=MGtest0000000000000000000000000000", sentBody);
        Assert.Contains("ScheduleType=fixed", sentBody);
        Assert.Contains("SendAt=", sentBody);
        Assert.DoesNotContain("From=%2B", sentBody);
    }

    [Fact]
    public async Task CancelScheduledAsync_PostsCanceledStatus()
    {
        var provider = ProviderReturning(_ => StubHandler.Json(HttpStatusCode.OK, """
            { "sid": "SMbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "status": "canceled", "to": "+14155552671",
              "from": null, "body": "later", "error_code": null, "error_message": null,
              "date_created": null, "date_sent": null, "date_updated": null }
            """), out var handler);

        await provider.CancelScheduledAsync("SMbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

        var request = handler.LastRequest!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/Messages/SMbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.json", request.RequestUri!.AbsolutePath);
        var sentBody = handler.LastRequestBody;
        Assert.Contains("Status=canceled", sentBody);
    }

    [Fact]
    public async Task RedactMessageBodyAsync_PostsEmptyBody()
    {
        var provider = ProviderReturning(_ => StubHandler.Json(HttpStatusCode.OK, """
            { "sid": "SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "status": "sent", "to": "+14155552671",
              "from": "+15550001111", "body": "", "error_code": null, "error_message": null,
              "date_created": null, "date_sent": null, "date_updated": null }
            """), out var handler);

        await provider.RedactMessageBodyAsync("SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        var sentBody = handler.LastRequestBody;
        Assert.Contains("Body=", sentBody);
        Assert.DoesNotContain("Status=", sentBody);
    }

    [Fact]
    public async Task GetMessageStateAsync_MapsOutcome()
    {
        var provider = ProviderReturning(_ => StubHandler.Json(HttpStatusCode.OK, """
            { "sid": "SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "status": "undelivered", "to": "+14155552671",
              "from": "+15550001111", "body": "hello", "error_code": 30006, "error_message": "Landline or unreachable carrier",
              "date_created": "2026-08-31T10:00:00Z", "date_sent": "2026-08-31T10:00:05Z", "date_updated": "2026-08-31T10:01:00Z" }
            """), out _);

        var state = await provider.GetMessageStateAsync("SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.Equal("undelivered", state.Status);
        Assert.Equal(30006, state.ErrorCode);
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 10, 0, 5, TimeSpan.Zero), state.DateSent);
    }

    [Fact]
    public async Task ListMessagesAsync_FiltersServerSideAndPagesToTheEnd()
    {
        var provider = ProviderReturning((request, callIndex) =>
        {
            if (callIndex == 0)
            {
                return StubHandler.Json(HttpStatusCode.OK, """
                    { "messages": [ { "sid": "SM00000000000000000000000000000001", "status": "delivered", "to": "+14155552671",
                        "from": "+15550001111", "body": "a", "error_code": null, "error_message": null,
                        "date_created": "2026-08-31T10:00:00Z", "date_sent": "2026-08-31T10:00:05Z", "date_updated": null } ],
                      "next_page_uri": "https://api.twilio.com/2010-04-01/Accounts/ACtest/Messages.json?PageSize=1000&Page=1&PageToken=PTNEXT",
                      "page": 0, "page_size": 1000, "uri": "", "first_page_uri": "", "previous_page_uri": null, "start": 0, "end": 0 }
                    """);
            }
            return StubHandler.Json(HttpStatusCode.OK, """
                { "messages": [ { "sid": "SM00000000000000000000000000000002", "status": "sent", "to": "+14155552671",
                    "from": "+15550001111", "body": "b", "error_code": null, "error_message": null,
                    "date_created": "2026-08-31T11:00:00Z", "date_sent": "2026-08-31T11:00:05Z", "date_updated": null } ],
                  "next_page_uri": null, "page": 1, "page_size": 1000, "uri": "", "first_page_uri": "", "previous_page_uri": null, "start": 1, "end": 1 }
                """);
        }, out var handler);

        var from = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var records = await provider.ListMessagesAsync(from, to);

        Assert.Equal(2, records.Count);
        Assert.Equal("SM00000000000000000000000000000001", records[0].MessageSid);
        Assert.Equal("SM00000000000000000000000000000002", records[1].MessageSid);

        Assert.Equal(2, handler.Requests.Count);
        var firstQuery = Uri.UnescapeDataString(handler.Requests[0].RequestUri!.Query);
        Assert.Contains("From=%2B15550001111".Replace("%2B", "+"), firstQuery);
        Assert.Contains("DateSent<", firstQuery);
        Assert.Contains("DateSent>", firstQuery);
        var secondQuery = Uri.UnescapeDataString(handler.Requests[1].RequestUri!.Query);
        Assert.Contains("PageToken=PTNEXT", secondQuery);
    }

    [Fact]
    public async Task ListMessagesAsync_StopsWhenTokenStopsAdvancing()
    {
        // A provider that keeps handing out the same next page must not spin forever.
        var provider = ProviderReturning(_ => StubHandler.Json(HttpStatusCode.OK, """
            { "messages": [ { "sid": "SM00000000000000000000000000000001", "status": "sent", "to": "+14155552671",
                "from": "+15550001111", "body": "a", "error_code": null, "error_message": null,
                "date_created": "2026-08-31T10:00:00Z", "date_sent": "2026-08-31T10:00:05Z", "date_updated": null } ],
              "next_page_uri": "https://api.twilio.com/2010-04-01/Accounts/ACtest/Messages.json?PageSize=1000&Page=1&PageToken=PTSAME",
              "page": 0, "page_size": 1000, "uri": "", "first_page_uri": "", "previous_page_uri": null, "start": 0, "end": 0 }
            """), out var handler);

        var records = await provider.ListMessagesAsync(
            new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Single(records.Select(r => r.MessageSid).Distinct());
    }
}

