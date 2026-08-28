using System.Net;
using System.Text;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class TwilioSmsProviderTests
{
    private const string MessageSid = "SM11111111111111111111111111111111";

    [Fact]
    public async Task UsesLookupHostAndCanonicalResponse()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            """{"valid":true,"phone_number":"+14165550100","validation_errors":null}"""));
        var provider = CreateProvider(handler);

        var result = await provider.ValidatePhoneNumberAsync("(416) 555-0100", default);

        Assert.True(result.IsValid);
        Assert.Equal("+14165550100", result.CanonicalPhoneNumber);
        Assert.StartsWith("https://lookups.twilio.com/v2/PhoneNumbers/", handler.Requests.Single().Uri);
    }

    [Fact]
    public async Task UsesMessagingOverrideAndVerifiedMessageFields()
    {
        var handler = new RecordingHandler(request => Json(HttpStatusCode.Created,
            MessageJson(request.Form.TryGetValue("Body", out var body) ? body : "", "queued")));
        var provider = CreateProvider(handler);

        await provider.SendAsync("+14165550100", "placed", default);
        await provider.ScheduleAsync("+14165550100", "follow up", DateTimeOffset.UtcNow.AddDays(3), default);
        await provider.CancelAsync(MessageSid, default);
        await provider.DisposeContentAsync(MessageSid, default);

        Assert.All(handler.Requests, x => Assert.StartsWith("https://messaging.test.example/root/2010-04-01/", x.Uri));
        Assert.Equal("+15551234567", handler.Requests[0].Form["From"]);
        Assert.Equal("MG11111111111111111111111111111111", handler.Requests[0].Form["MessagingServiceSid"]);
        Assert.Equal("fixed", handler.Requests[1].Form["ScheduleType"]);
        Assert.True(DateTimeOffset.TryParse(handler.Requests[1].Form["SendAt"], out _));
        Assert.Equal("canceled", handler.Requests[2].Form["Status"]);
        Assert.True(handler.Requests[3].Form.ContainsKey("Body"));
        Assert.Equal(string.Empty, handler.Requests[3].Form["Body"]);
    }

    [Fact]
    public async Task ReconciliationIsServerFilteredByFromAndFollowsEveryPageOnOverride()
    {
        var page = 0;
        var handler = new RecordingHandler(_ =>
        {
            page++;
            var next = page == 1
                ? "\"/2010-04-01/Accounts/AC11111111111111111111111111111111/Messages.json?PageToken=next\""
                : "null";
            return Json(HttpStatusCode.OK, $"{{\"messages\":[{MessageJson("body", "delivered")}],\"next_page_uri\":{next}}}");
        });
        var provider = CreateProvider(handler);

        var result = await provider.ListAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), default);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("From=%2B15551234567", handler.Requests[0].Uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DateSent%3E=", handler.Requests[0].Uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DateSent%3C=", handler.Requests[0].Uri, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://messaging.test.example/root/", handler.Requests[1].Uri);
    }

    private static TwilioSmsProvider CreateProvider(HttpMessageHandler handler) => new(new HttpClient(handler),
        Options.Create(new TwilioOptions
        {
            AccountSid = "AC11111111111111111111111111111111",
            AuthToken = "secret",
            FromNumber = "+15551234567",
            MessagingServiceSid = "MG11111111111111111111111111111111",
            BaseUrl = "https://messaging.test.example/root"
        }));

    private static string MessageJson(string body, string status) =>
        $$"""{"sid":"{{MessageSid}}","status":"{{status}}","body":"{{body}}","from":"+15551234567","to":"+14165550100","error_code":null,"date_created":"Fri, 28 Aug 2026 01:00:00 +0000","date_sent":"Fri, 28 Aug 2026 01:00:01 +0000"}""";

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<RecordedRequest, HttpResponseMessage> _respond;
        public List<RecordedRequest> Requests { get; } = new();

        public RecordingHandler(Func<RecordedRequest, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var form = content.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Split('=', 2))
                .ToDictionary(x => Uri.UnescapeDataString(x[0]), x => Uri.UnescapeDataString(x.Length > 1 ? x[1].Replace('+', ' ') : ""));
            var recorded = new RecordedRequest(request.RequestUri!.AbsoluteUri, form);
            Requests.Add(recorded);
            return _respond(recorded);
        }
    }

    private sealed record RecordedRequest(string Uri, IReadOnlyDictionary<string, string> Form);
}
