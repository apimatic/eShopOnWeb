using System.Net;
using System.Text;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Twilio;

public class TwilioClientTests
{
    [Fact]
    public async Task LookupUsesDedicatedHostAndReturnsCanonicalNumber()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            "{\"valid\":true,\"phone_number\":\"canonical-destination\"}"));
        var client = CreateClient(handler);

        var result = await client.ValidatePhoneNumberAsync("typed destination", default);

        Assert.True(result.IsValid);
        Assert.Equal("canonical-destination", result.CanonicalNumber);
        Assert.Equal("lookups.twilio.com", handler.Requests.Single().Uri.Host);
        Assert.Contains("typed%20destination", handler.Requests.Single().Uri.AbsoluteUri);
    }

    [Fact]
    public async Task LookupBadRequestIsAnInvalidDestination()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.BadRequest, "{}"));
        var client = CreateClient(handler);

        var result = await client.ValidatePhoneNumberAsync("invalid destination", default);

        Assert.False(result.IsValid);
        Assert.Null(result.CanonicalNumber);
    }

    [Fact]
    public async Task ScheduledSendUsesMessagingOverrideAndRequiredFormFields()
    {
        var handler = new RecordingHandler(_ => Message("scheduled", "message-one"));
        var client = CreateClient(handler);

        await client.SendMessageAsync("destination", "follow up", DateTimeOffset.UtcNow.AddDays(3), default);

        var request = handler.Requests.Single();
        Assert.StartsWith("https://messaging.test/2010-04-01/Accounts/account/Messages.json", request.Uri.AbsoluteUri);
        Assert.Contains("To=destination", request.Body);
        Assert.Contains("From=sender", request.Body);
        Assert.Contains("MessagingServiceSid=service", request.Body);
        Assert.Contains("ScheduleType=fixed", request.Body);
        Assert.Contains("SendAt=", request.Body);
    }

    [Fact]
    public async Task ReconciliationFiltersAtProviderAndFollowsAllPagesThroughOverride()
    {
        var handlerCallCount = 0;
        var handler = new RecordingHandler(request => handlerResponse(request));
        HttpResponseMessage handlerResponse(HttpRequestMessage request)
        {
            return handlerCallCount++ == 0
                ? Json(HttpStatusCode.OK,
                    "{\"messages\":[],\"next_page_uri\":\"/2010-04-01/Accounts/account/Messages.json?PageToken=next\"}")
                : Json(HttpStatusCode.OK, "{\"messages\":[],\"next_page_uri\":null}");
        }
        var client = CreateClient(handler);

        await client.ListMessagesAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), default);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, x => Assert.Equal("messaging.test", x.Uri.Host));
        Assert.Contains("From=sender", handler.Requests[0].Uri.Query);
        Assert.Contains("DateSent%3E=", handler.Requests[0].Uri.Query);
        Assert.Contains("DateSent%3C=", handler.Requests[0].Uri.Query);
        Assert.Contains("PageToken=next", handler.Requests[1].Uri.Query);
    }

    [Theory]
    [InlineData(true, "Body=")]
    [InlineData(false, "Status=canceled")]
    public async Task MessageUpdatesUseFormEncoding(bool redact, string expectedBody)
    {
        var handler = new RecordingHandler(request => redact && request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK,
                "{\"sid\":\"message-one\",\"status\":\"delivered\",\"error_code\":null,\"body\":\"\",\"date_created\":null,\"date_sent\":null}")
            : Message(redact ? "delivered" : "canceled", "message-one"));
        var client = CreateClient(handler);

        if (redact)
            await client.RedactMessageAsync("message-one", default);
        else
            await client.CancelMessageAsync("message-one", default);

        Assert.Equal(expectedBody, handler.Requests.First().Body);
        Assert.Equal(redact ? 2 : 1, handler.Requests.Count);
    }

    private static TwilioClient CreateClient(HttpMessageHandler handler)
    {
        var settings = Options.Create(new TwilioSettings
        {
            AccountSid = "account",
            AuthToken = "secret",
            FromNumber = "sender",
            MessagingServiceSid = "service",
            BaseUrl = "https://messaging.test"
        });
        return new TwilioClient(new HttpClient(handler), settings);
    }

    private static HttpResponseMessage Message(string status, string sid) =>
        Json(HttpStatusCode.Created,
            $"{{\"sid\":\"{sid}\",\"status\":\"{status}\",\"error_code\":null,\"body\":\"content\",\"date_created\":\"Thu, 27 Aug 2026 20:00:00 +0000\",\"date_sent\":null}}");

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;
        public List<RecordedRequest> Requests { get; } = new();

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(request.RequestUri!,
                request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return _response(request);
        }
    }

    private sealed record RecordedRequest(Uri Uri, string? Body);
}
