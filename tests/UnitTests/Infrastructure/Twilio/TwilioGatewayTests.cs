using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Twilio;

public class TwilioGatewayTests
{
    [Fact]
    public async Task UsesSpecHostsFormsFiltersAndEveryPaginationPage()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var options = Options.Create(new TwilioOptions
        {
            AccountSid = "AC" + new string('a', 32),
            AuthToken = "secret-token",
            FromNumber = "+15550002222",
            MessagingServiceSid = "MG" + new string('b', 32),
            BaseUrl = "https://mock.local/twilio"
        });
        using var gateway = new TwilioGateway(options, client);

        var validation = await gateway.ValidatePhoneNumberAsync("+1 (555) 000-1111", default);
        Assert.True(validation.IsValid);
        Assert.Equal("+15550001111", validation.CanonicalNumber);

        var scheduledFor = DateTimeOffset.Parse("2026-09-01T12:00:00Z");
        await gateway.SendMessageAsync("+15550001111", "scheduled body", scheduledFor, default);
        await gateway.RedactMessageContentAsync("SM" + new string('c', 32), default);
        var listed = await gateway.ListMessagesAsync(
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-31T23:59:59Z"), default);

        Assert.Equal(2, listed.Count);
        Assert.StartsWith("https://lookups.twilio.com/v2/PhoneNumbers/", handler.Requests[0].Uri);
        Assert.DoesNotContain("mock.local", handler.Requests[0].Uri);

        var send = handler.Requests[1];
        Assert.StartsWith("https://mock.local/twilio/2010-04-01/Accounts/", send.Uri);
        Assert.Contains("To=%2B15550001111", send.Body);
        Assert.Contains("From=%2B15550002222", send.Body);
        Assert.Contains("MessagingServiceSid=MG", send.Body);
        Assert.Contains("ScheduleType=fixed", send.Body);
        Assert.Contains("SendAt=", send.Body);

        Assert.Equal("Body=", handler.Requests[2].Body);
        var listRequests = handler.Requests.Where(request => request.Method == HttpMethod.Get &&
            request.Uri.Contains("Messages.json", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, listRequests.Length);
        Assert.All(listRequests, request => Assert.Contains("From=%2B15550002222", request.Uri));
        Assert.Contains("DateSent%3E=", listRequests[0].Uri);
        Assert.Contains("DateSent%3C=", listRequests[0].Uri);
        Assert.All(handler.Requests, request => Assert.Equal("Basic", request.Authorization?.Scheme));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = new();
        private int _listPage;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!.OriginalString, body,
                request.Headers.Authorization));

            if (request.RequestUri.Host == "lookups.twilio.com")
                return Json(HttpStatusCode.OK, """{"phone_number":"+15550001111","valid":true}""");

            if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath.EndsWith("Messages.json"))
            {
                _listPage++;
                return _listPage == 1
                    ? Json(HttpStatusCode.OK, """{"messages":[{"sid":"SM11111111111111111111111111111111","status":"delivered","from":"+15550002222"}],"next_page_uri":"/2010-04-01/Accounts/ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/Messages.json?PageToken=next"}""")
                    : Json(HttpStatusCode.OK, """{"messages":[{"sid":"SM22222222222222222222222222222222","status":"undelivered","from":"+15550002222","error_code":30034}],"next_page_uri":null}""");
            }

            return Json(HttpStatusCode.OK,
                """{"sid":"SMcccccccccccccccccccccccccccccccc","status":"scheduled","body":"","from":"+15550002222","to":"+15550001111"}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed record CapturedRequest(HttpMethod Method, string Uri, string Body,
        AuthenticationHeaderValue? Authorization);
}
