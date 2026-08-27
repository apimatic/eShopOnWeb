using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using NSubstitute;
using TwilioSdk;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services;

public class TwilioSmsServiceTests
{
    private static readonly TwilioOptions Options = new()
    {
        AccountSid = "AC-test",
        AuthToken = "test-token",
        FromNumber = "+15550001111",
        MessagingServiceSid = "MG-test"
    };

    private static TwilioSmsService ServiceReturning(HttpStatusCode status, string json, out StubHandler handler)
    {
        handler = new StubHandler(_ => StubHandler.Json(status, json));
        var client = new TwilioSdkClient(new HttpClient(handler), new TwilioSdkClientOptions());
        return new TwilioSmsService(client, Microsoft.Extensions.Options.Options.Create(Options),
            Substitute.For<IAppLogger<TwilioSmsService>>());
    }

    private static string LastRequestBody(StubHandler handler) =>
        WebUtility.UrlDecode(handler.LastRequestBody ?? string.Empty);

    [Fact]
    public async Task SendSms_SendsFromConfiguredNumber_AndReturnsSidAndStatus()
    {
        var service = ServiceReturning(HttpStatusCode.Created,
            """{ "sid": "SM123", "status": "queued", "to": "+15550002222", "from": "+15550001111" }""", out var handler);

        var result = await service.SendSmsAsync("+15550002222", "hello");

        Assert.Equal("SM123", result.MessageSid);
        Assert.Equal("queued", result.Status);

        var body = LastRequestBody(handler);
        Assert.Contains("To=+15550002222", body);
        Assert.Contains("From=+15550001111", body);
        Assert.Contains("hello", body);
        Assert.DoesNotContain("MessagingServiceSid", body);
    }

    [Fact]
    public async Task SendSms_ProviderRejection_ThrowsSmsProviderExceptionWithStatus()
    {
        var service = ServiceReturning(HttpStatusCode.BadRequest,
            """{ "code": 21211, "message": "Invalid To" }""", out _);

        var ex = await Assert.ThrowsAsync<SmsProviderException>(() => service.SendSmsAsync("+15550002222", "hello"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.ProviderStatusCode);
    }

    [Fact]
    public async Task SendSms_TransportFailure_BlocksDuplicateAttempt()
    {
        var stub = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        var guarded = new SingleSendGuardHandler { InnerHandler = stub };
        var client = new TwilioSdkClient(new HttpClient(guarded), new TwilioSdkClientOptions());
        var service = new TwilioSmsService(client, Microsoft.Extensions.Options.Options.Create(Options),
            Substitute.For<IAppLogger<TwilioSmsService>>());

        // The SDK retries transport failures on every verb; the guard must hold the count at one.
        await Assert.ThrowsAsync<SmsProviderException>(() => service.SendSmsAsync("+15550002222", "hello"));

        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task ScheduleSms_UsesMessagingService_WithFixedScheduleType()
    {
        var service = ServiceReturning(HttpStatusCode.Created,
            """{ "sid": "SM-sched", "status": "scheduled" }""", out var handler);
        var sendAt = DateTimeOffset.UtcNow.AddDays(3);

        var result = await service.ScheduleSmsAsync("+15550002222", "follow up", sendAt);

        Assert.Equal("SM-sched", result.MessageSid);
        Assert.Equal("scheduled", result.Status);

        var body = LastRequestBody(handler);
        Assert.Contains("MessagingServiceSid=MG-test", body);
        Assert.Contains("ScheduleType=fixed", body);
        Assert.Contains("SendAt=", body);
        Assert.DoesNotContain("From=+15550001111", body);
    }

    [Fact]
    public async Task ValidatePhoneNumber_Valid_ReturnsCanonicalForm()
    {
        var service = ServiceReturning(HttpStatusCode.OK,
            """{ "valid": true, "phone_number": "+14155552671", "national_format": "(415) 555-2671" }""", out _);

        var result = await service.ValidatePhoneNumberAsync("4155552671");

        Assert.True(result.IsValid);
        Assert.Equal("+14155552671", result.CanonicalNumber);
    }

    [Fact]
    public async Task ValidatePhoneNumber_Invalid_ReturnsNotUsable()
    {
        var service = ServiceReturning(HttpStatusCode.OK,
            """{ "valid": false, "validation_errors": ["TOO_SHORT"] }""", out _);

        var result = await service.ValidatePhoneNumberAsync("123");

        Assert.False(result.IsValid);
        Assert.Null(result.CanonicalNumber);
        Assert.Contains("TOO_SHORT", result.FailureReason);
    }

    [Fact]
    public async Task ValidatePhoneNumber_Provider404_ReturnsNotUsable()
    {
        var service = ServiceReturning(HttpStatusCode.NotFound, """{ "message": "not found" }""", out _);

        var result = await service.ValidatePhoneNumberAsync("not-a-number");

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task CancelScheduled_SendsCanceledStatus()
    {
        var service = ServiceReturning(HttpStatusCode.OK,
            """{ "sid": "SM-sched", "status": "canceled" }""", out var handler);

        await service.CancelScheduledSmsAsync("SM-sched");

        var body = LastRequestBody(handler);
        Assert.Contains("Status=canceled", body);
    }

    [Fact]
    public async Task RedactMessageBody_SendsEmptyBody_AndKeepsRecord()
    {
        var service = ServiceReturning(HttpStatusCode.OK,
            """{ "sid": "SM123", "status": "delivered", "body": "" }""", out var handler);

        await service.RedactMessageBodyAsync("SM123");

        var body = LastRequestBody(handler);
        Assert.Contains("Body=", body);
        Assert.DoesNotContain("Status=", body);
    }

    [Fact]
    public async Task ListSentMessages_FiltersByFromAndRange_AndStopsWhenNoNextPage()
    {
        var service = ServiceReturning(HttpStatusCode.OK,
            """{ "messages": [ { "sid": "SM1", "status": "delivered", "date_sent": "Thu, 27 Aug 2026 10:00:00 +0000" } ], "next_page_uri": null, "page": 0, "page_size": 100 }""",
            out var handler);
        var from = new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);

        var result = await service.ListSentMessagesAsync(from, to);

        var record = Assert.Single(result.Messages);
        Assert.Equal("SM1", record.MessageSid);
        Assert.Equal("delivered", record.Status);
        Assert.False(result.Truncated);
        Assert.Equal(Options.FromNumber, result.FromNumber);

        var query = WebUtility.UrlDecode(handler.LastRequest!.RequestUri!.Query);
        Assert.Contains("From=+15550001111", query);
        Assert.Contains("DateSent<", query);
        Assert.Contains("DateSent>", query);
        // Page-number paging is rejected by the provider; only cursor paging may be used.
        Assert.DoesNotContain("Page=", query);
    }

    [Fact]
    public async Task ListSentMessages_FollowsPageTokenCursor()
    {
        var calls = 0;
        var stub = new StubHandler(_ =>
        {
            calls++;
            return calls == 1
                ? StubHandler.Json(HttpStatusCode.OK,
                    """{ "messages": [ { "sid": "SM1", "status": "delivered" } ], "next_page_uri": "https://api.twilio.com/2010-04-01/Accounts/AC-test/Messages.json?PageSize=100&PageToken=PASMabc" }""")
                : StubHandler.Json(HttpStatusCode.OK,
                    """{ "messages": [ { "sid": "SM2", "status": "queued" } ], "next_page_uri": null }""");
        });
        var client = new TwilioSdkClient(new HttpClient(stub), new TwilioSdkClientOptions());
        var service = new TwilioSmsService(client, Microsoft.Extensions.Options.Options.Create(Options),
            Substitute.For<IAppLogger<TwilioSmsService>>());
        var from = new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);

        var result = await service.ListSentMessagesAsync(from, to);

        Assert.Equal(2, calls);
        Assert.Equal(new[] { "SM1", "SM2" }, result.Messages.Select(m => m.MessageSid).ToArray());
        Assert.False(result.Truncated);

        var secondQuery = WebUtility.UrlDecode(stub.Requests[1].RequestUri!.Query);
        Assert.Contains("PageToken=PASMabc", secondQuery);
        Assert.DoesNotContain("Page=", secondQuery);
    }

    [Fact]
    public async Task MalformedSuccessBody_ThrowsSmsProviderException()
    {
        var service = ServiceReturning(HttpStatusCode.Created, """not json at all""", out _);

        await Assert.ThrowsAsync<SmsProviderException>(() => service.SendSmsAsync("+15550002222", "hello"));
    }
}
