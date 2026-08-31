using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TwilioSdk;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Notifications;

public class TwilioNotificationGatewayTests
{
    private const string AccountSid = "ACtest";
    private const string FromNumber = "+1555000111";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> RequestBodies { get; } = new();
        public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];
        public string LastRequestBody => RequestBodies.Count == 0 ? string.Empty : RequestBodies[^1];

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            // The SDK disposes the request after sending, so capture the body here.
            RequestBodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(ct));
            return _responder(request);
        }
    }

    private static TwilioNotificationGateway GatewayReturning(StubHandler handler)
    {
        var client = new TwilioSdkClient(new HttpClient(handler), new TwilioSdkClientOptions());
        var settings = Options.Create(new TwilioSettings
        {
            AccountSid = AccountSid,
            AuthToken = "test",
            FromNumber = FromNumber,
            MessagingServiceSid = "MGtest"
        });
        return new TwilioNotificationGateway(client, settings, NullLogger<TwilioNotificationGateway>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task SendMessageAsync_ReturnsSidAndStatus()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created,
            """{ "sid": "SM123", "status": "queued", "to": "+1555000222", "from": "+1555000111", "body": "hi" }"""));
        var gateway = GatewayReturning(handler);

        var result = await gateway.SendMessageAsync("+1555000222", "hi");

        Assert.Equal("SM123", result.Sid);
        Assert.Equal("queued", result.Status);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("/2010-04-01/Accounts/", handler.LastRequest.RequestUri!.AbsolutePath);
        var sentBody = handler.LastRequestBody;
        Assert.Contains("To=", sentBody);
        Assert.Contains("From=", sentBody);
    }

    [Fact]
    public async Task SendMessageAsync_ApiError_ThrowsProviderExceptionWithStatus()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.BadRequest, """{ "code": 21211, "message": "invalid" }"""));
        var gateway = GatewayReturning(handler);

        var ex = await Assert.ThrowsAsync<NotificationProviderException>(
            () => gateway.SendMessageAsync("+1555000222", "hi"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task ValidatePhoneNumberAsync_ValidNumber_ReturnsCanonicalForm()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{ "valid": true, "phone_number": "+1555000222", "national_format": "(555) 000-222", "country_code": "US", "calling_country_code": "1" }"""));
        var gateway = GatewayReturning(handler);

        var result = await gateway.ValidatePhoneNumberAsync("555000222");

        Assert.Equal("+1555000222", result.CanonicalNumber);
        Assert.Contains("/v2/PhoneNumbers/", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ValidatePhoneNumberAsync_InvalidNumber_ThrowsInvalidPhoneNumber()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{ "valid": false, "validation_errors": ["TOO_SHORT"] }"""));
        var gateway = GatewayReturning(handler);

        await Assert.ThrowsAsync<InvalidPhoneNumberException>(() => gateway.ValidatePhoneNumberAsync("123"));
    }

    [Fact]
    public async Task ValidatePhoneNumberAsync_Provider4xx_ThrowsInvalidPhoneNumber()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, """{ "message": "not found" }"""));
        var gateway = GatewayReturning(handler);

        await Assert.ThrowsAsync<InvalidPhoneNumberException>(() => gateway.ValidatePhoneNumberAsync("garbage"));
    }

    [Fact]
    public async Task ScheduleMessageAsync_UsesMessagingServiceAndSendAt()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created,
            """{ "sid": "SMsched", "status": "scheduled", "to": "+1555000222", "body": "later" }"""));
        var gateway = GatewayReturning(handler);

        var sendAt = DateTimeOffset.UtcNow.AddDays(3);
        var result = await gateway.ScheduleMessageAsync("+1555000222", "later", sendAt);

        Assert.Equal("SMsched", result.Sid);
        Assert.Equal("scheduled", result.Status);

        var sentBody = handler.LastRequestBody;
        Assert.Contains("MessagingServiceSid=", sentBody);
        Assert.Contains("ScheduleType=fixed", sentBody);
        Assert.Contains("SendAt=", sentBody);
        Assert.DoesNotContain("From=", sentBody);
    }

    [Fact]
    public async Task CancelScheduledMessageAsync_PostsCanceledStatus()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{ "sid": "SMsched", "status": "canceled" }"""));
        var gateway = GatewayReturning(handler);

        var result = await gateway.CancelScheduledMessageAsync("SMsched");

        Assert.Equal("canceled", result.Status);
        var sentBody = handler.LastRequestBody;
        Assert.Contains("Status=canceled", sentBody);
    }

    [Fact]
    public async Task ListMessagesAsync_PagesThroughWholeRange_AndFiltersByFromNumber()
    {
        var page1 = """{ "messages": [ { "sid": "SM1", "status": "delivered" } ], "next_page_uri": "/2010-04-01/Accounts/ACtest/Messages.json?PageSize=100&Page=1&PageToken=PT2", "page": 0, "page_size": 100 }""";
        var page2 = """{ "messages": [ { "sid": "SM2", "status": "sent" } ], "next_page_uri": null, "page": 1, "page_size": 100 }""";
        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            calls++;
            return Json(HttpStatusCode.OK, calls == 1 ? page1 : page2);
        });
        var gateway = GatewayReturning(handler);

        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow;
        var results = await gateway.ListMessagesAsync(from, to);

        Assert.Equal(2, results.Count);
        Assert.Equal(2, calls);
        var query = handler.Requests[0].RequestUri!.Query;
        Assert.Contains("From=", query);
        Assert.Contains("DateSent%3C=", query); // upper bound
        Assert.Contains("DateSent%3E=", query); // lower bound
        Assert.Contains("PageToken=PT2", handler.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task RedactMessageBodyAsync_PostsEmptyBody()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{ "sid": "SM1", "status": "delivered", "body": "" }"""));
        var gateway = GatewayReturning(handler);

        await gateway.RedactMessageBodyAsync("SM1");

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        var sentBody = handler.LastRequestBody;
        Assert.Contains("Body=", sentBody);
    }
}
