using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.NotificationEndpoints;

[TestClass]
public class TwilioMessageProviderTest
{
    [TestMethod]
    public async Task MessagingOverrideIsUsedForSendFetchUpdateAndEveryListPage()
    {
        var handler = new RecordingHandler();
        var options = new TwilioOptions
        {
            AccountSid = "account",
            AuthToken = "secret",
            FromNumber = "application-sender",
            MessagingServiceSid = "service",
            BaseUrl = "https://messaging.test.example/base"
        };
        using var provider = new TwilioMessageProvider(options, new HttpClient(handler));

        var scheduled = await provider.SendAsync("destination", "scheduled body",
            DateTimeOffset.UtcNow.AddDays(3));
        await provider.GetAsync(scheduled.Id);
        await provider.CancelAsync(scheduled.Id);
        await provider.RedactAsync(scheduled.Id);
        var listed = await provider.ListApplicationMessagesAsync(
            DateTimeOffset.Parse("2026-08-28T07:00:00Z"), DateTimeOffset.Parse("2026-08-28T09:00:00Z"));

        Assert.AreEqual(2, listed.Count);
        Assert.IsTrue(handler.Requests.TrueForAll(x =>
            x.Uri.StartsWith("https://messaging.test.example/base/", StringComparison.Ordinal)));
        StringAssert.Contains(handler.Requests[0].Body!, "ScheduleType=fixed");
        StringAssert.Contains(handler.Requests[0].Body!, "MessagingServiceSid=service");
        StringAssert.Contains(handler.Requests[4].Uri, "From=application-sender");
        StringAssert.Contains(handler.Requests[4].Uri, "DateSent=2026-08-28");
        Assert.IsTrue(handler.Requests[5].Uri.StartsWith(
            "https://messaging.test.example/base/2010-04-01/", StringComparison.Ordinal));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Uri, string? Body)> Requests { get; } = new();
        private int _listPage;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.ToString(), body));
            string json;
            if (request.Method == HttpMethod.Get && (request.RequestUri.Query.Contains("From=")
                || request.RequestUri.Query.Contains("PageToken=")))
            {
                _listPage++;
                var next = _listPage == 1
                    ? "\"https://api.twilio.com/2010-04-01/Accounts/account/Messages.json?PageToken=next\""
                    : "null";
                json = $"{{\"messages\":[{MessageJson($"SM{_listPage:D32}")}],\"next_page_uri\":{next}}}";
            }
            else
            {
                json = MessageJson("SM00000000000000000000000000000001");
            }
            return new HttpResponseMessage(request.Method == HttpMethod.Post && body?.Contains("Body=scheduled") == true
                ? HttpStatusCode.Created : HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        private static string MessageJson(string sid) =>
            $"{{\"sid\":\"{sid}\",\"status\":\"scheduled\",\"error_code\":null," +
            "\"date_created\":\"Fri, 28 Aug 2026 08:00:00 +0000\",\"date_sent\":null}";
    }
}
