using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

[TestClass]
public class TwilioMessagingServiceTest
{
    [TestMethod]
    public async Task UsesVerifiedContractsAndMessagingBaseUrlForEveryMessagingCall()
    {
        var handler = new RecordingHandler();
        var options = Options.Create(new TwilioOptions
        {
            AccountSid = "AC00000000000000000000000000000000",
            AuthToken = "secret-not-a-real-token",
            FromNumber = "+15550001111",
            MessagingServiceSid = "MG00000000000000000000000000000000",
            BaseUrl = "https://messaging.test/custom/"
        });
        using var service = new TwilioMessagingService(options, handler);

        var normalized = await service.ValidateAndNormalizeAsync("+1 555 000 2222", CancellationToken.None);
        await service.SendAsync("+15550002222", "placed", CancellationToken.None);
        await service.ScheduleAsync("+15550002222", "follow up", DateTimeOffset.Parse("2030-01-05T12:00:00Z"), CancellationToken.None);
        await service.FetchAsync(RecordingHandler.MessageSid, CancellationToken.None);
        await service.CancelAsync(RecordingHandler.MessageSid, CancellationToken.None);
        await service.RedactAsync(RecordingHandler.MessageSid, CancellationToken.None);
        var messages = await service.ListAsync(
            DateTimeOffset.Parse("2029-12-31T00:00:00Z"),
            DateTimeOffset.Parse("2030-01-02T00:00:00Z"),
            CancellationToken.None);

        Assert.AreEqual("+15550002222", normalized);
        Assert.AreEqual(2, messages.Count);
        Assert.IsTrue(handler.Requests.Where(x => !x.Uri.Host.Equals("lookups.twilio.com", StringComparison.OrdinalIgnoreCase))
            .All(x => x.Uri.AbsoluteUri.StartsWith("https://messaging.test/custom/", StringComparison.Ordinal)));
        Assert.IsTrue(handler.Requests.Any(x => x.Body.Contains("MessagingServiceSid=MG", StringComparison.Ordinal)
            && x.Body.Contains("ScheduleType=fixed", StringComparison.Ordinal)
            && x.Body.Contains("From=%2B15550001111", StringComparison.Ordinal)));
        Assert.IsTrue(handler.Requests.Any(x => x.Body == "Status=canceled"));
        Assert.IsTrue(handler.Requests.Any(x => x.Body == "Body="));
        Assert.IsTrue(handler.Requests.Where(x => x.Uri.Query.Contains("PageSize", StringComparison.Ordinal))
            .All(x => x.Uri.Query.Contains("From=%2B15550001111", StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public const string MessageSid = "SM00000000000000000000000000000001";
        public List<(Uri Uri, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!, body));

            if (request.RequestUri!.Host == "lookups.twilio.com")
                return Json("{\"valid\":true,\"phone_number\":\"+15550002222\"}");

            if (request.RequestUri.Query.Contains("PageToken=second", StringComparison.Ordinal))
                return Json(Page(null, "SM00000000000000000000000000000003"));
            if (request.RequestUri.Query.Contains("PageSize", StringComparison.Ordinal))
                return Json(Page("https://api.twilio.com/2010-04-01/Accounts/AC00000000000000000000000000000000/Messages.json?From=%2B15550001111&DateSent%3E=2029-12-31&DateSent%3C=2030-01-02&PageSize=1000&PageToken=second", MessageSid));

            var status = body.Contains("ScheduleType=fixed", StringComparison.Ordinal) ? "scheduled"
                : body == "Status=canceled" ? "canceled" : "queued";
            return Json(MessageSid, status, body == "Body=" ? string.Empty : "message");
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        private static HttpResponseMessage Json(string sid, string status, string body) => Json(
            $"{{\"sid\":\"{sid}\",\"status\":\"{status}\",\"body\":\"{body}\",\"date_created\":\"Tue, 01 Jan 2030 12:00:00 +0000\",\"date_sent\":\"Tue, 01 Jan 2030 12:00:01 +0000\",\"error_code\":null}}");

        private static string Page(string? nextPage, string sid) =>
            $"{{\"messages\":[{{\"sid\":\"{sid}\",\"status\":\"delivered\",\"body\":\"message\",\"date_created\":\"Tue, 01 Jan 2030 12:00:00 +0000\",\"date_sent\":\"Tue, 01 Jan 2030 12:00:01 +0000\"}}],\"next_page_uri\":{(nextPage is null ? "null" : $"\"{nextPage}\"")}}}";
    }
}
