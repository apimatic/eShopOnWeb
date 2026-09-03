using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Twilio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Messaging;

public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();
    public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];
    public string? LastBody => Bodies.Count == 0 ? null : Bodies[^1];

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : request.Content.ReadAsStringAsync().Result);
        var response = _responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}

public class TwilioMessagingGatewayTests
{
    private static readonly TwilioSettings Settings = new()
    {
        AccountSid = "ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        AuthToken = "token",
        FromNumber = "+15005550006",
        MessagingServiceSid = "MGaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    };

    private static TwilioMessagingGateway Gateway(StubHandler handler)
    {
        var client = new TwilioClient(new HttpClient(handler), new TwilioClientOptions());
        return new TwilioMessagingGateway(client, Options.Create(Settings), NullLogger<TwilioMessagingGateway>.Instance);
    }

    [Fact]
    public async Task LookupStoresCanonicalNumberWhenValid()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"phone_number":"+15551234567","valid":true}"""));
        var gateway = Gateway(handler);

        var result = await gateway.LookupNumberAsync("+1 555 123 4567", CancellationToken.None);

        Assert.True(result.IsUsable);
        Assert.Equal("+15551234567", result.CanonicalNumber);
        Assert.Contains("/v2/PhoneNumbers/", handler.LastRequest!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LookupRejectsInvalidNumber()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"phone_number":"+1555","valid":false,"validation_errors":["TOO_SHORT"]}"""));
        var gateway = Gateway(handler);

        var result = await gateway.LookupNumberAsync("555", CancellationToken.None);

        Assert.False(result.IsUsable);
        Assert.Null(result.CanonicalNumber);
    }

    [Fact]
    public async Task SendPostsToAndFromAndBody()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created,
            """{"sid":"SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","status":"queued","body":"hello"}"""));
        var gateway = Gateway(handler);

        var result = await gateway.SendAsync(new SendMessageRequest("+15551234567", "hello", false, null), CancellationToken.None);

        Assert.Equal("SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.Sid);
        Assert.Equal("queued", result.Status);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("/2010-04-01/Accounts/ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/Messages.json", handler.LastRequest.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("To=%2B15551234567", handler.LastBody);
        Assert.Contains("From=%2B15005550006", handler.LastBody);
        Assert.Contains("Body=hello", handler.LastBody);
        Assert.DoesNotContain("ScheduleType", handler.LastBody);
    }

    [Fact]
    public async Task ScheduledSendIncludesMessagingServiceAndSendAt()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created,
            """{"sid":"SMbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","status":"scheduled"}"""));
        var gateway = Gateway(handler);
        var sendAt = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

        var result = await gateway.SendAsync(new SendMessageRequest("+15551234567", "follow up", true, sendAt), CancellationToken.None);

        Assert.Equal("scheduled", result.Status);
        Assert.Contains("ScheduleType=fixed", handler.LastBody);
        Assert.Contains("MessagingServiceSid=MGaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", handler.LastBody);
        Assert.Contains("SendAt=", handler.LastBody);
    }

    [Fact]
    public async Task CreateMessageIsNotRetriedOn503()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var gateway = Gateway(handler);

        var result = await gateway.SendAsync(new SendMessageRequest("+15551234567", "hello", false, null), CancellationToken.None);

        Assert.Equal("send_failed", result.Status);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task CancelPostsCanceledStatus()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"sid":"SMcccccccccccccccccccccccccccccccc","status":"canceled"}"""));
        var gateway = Gateway(handler);

        var result = await gateway.CancelScheduledAsync("SMcccccccccccccccccccccccccccccccc", CancellationToken.None);

        Assert.Equal("canceled", result!.Status);
        Assert.Contains("Status=canceled", handler.LastBody);
    }

    [Fact]
    public async Task RedactPostsEmptyBody()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"sid":"SMdddddddddddddddddddddddddddddddd","status":"delivered","body":""}"""));
        var gateway = Gateway(handler);

        var result = await gateway.RedactBodyAsync("SMdddddddddddddddddddddddddddddddd", CancellationToken.None);

        Assert.Equal("", result!.Body);
        Assert.Contains("Body=", handler.LastBody);
        Assert.DoesNotContain("Status=", handler.LastBody);
    }

    [Fact]
    public async Task ListAsksProviderForConfiguredFromNumber()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"messages":[{"sid":"SMeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee","status":"delivered"}],"next_page_uri":null}"""));
        var gateway = Gateway(handler);

        var from = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);
        var list = await gateway.ListSentFromConfiguredNumberAsync(from, to, CancellationToken.None);

        Assert.Single(list.Messages);
        Assert.False(list.Truncated);
        Assert.Contains("From=%2B15005550006", handler.LastRequest!.RequestUri!.Query);
        Assert.Contains("DateSent%3E", handler.LastRequest.RequestUri!.Query);
        Assert.Contains("DateSent%3C", handler.LastRequest.RequestUri!.Query);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}
