using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.NotificationEndpoints;

[TestClass]
public class TwilioRestClientContractTests
{
    [TestMethod]
    public async Task MessagingOperationsUseSpecPathsFormsAuthOverrideAndAllPages()
    {
        var handler = new RecordingHandler();
        handler.Responses.Enqueue(Json(HttpStatusCode.Created,
            "{\"sid\":\"SM00000000000000000000000000000001\",\"status\":\"scheduled\"}"));
        handler.Responses.Enqueue(Json(HttpStatusCode.OK,
            "{\"sid\":\"SM00000000000000000000000000000001\",\"status\":\"canceled\"}"));
        handler.Responses.Enqueue(Json(HttpStatusCode.OK,
            "{\"sid\":\"SM00000000000000000000000000000001\",\"status\":\"sent\",\"body\":\"\"}"));
        handler.Responses.Enqueue(Json(HttpStatusCode.OK,
            "{\"messages\":[{\"sid\":\"SM00000000000000000000000000000001\",\"status\":\"delivered\",\"date_sent\":\"Fri, 28 Aug 2026 10:00:00 +0000\"}],\"next_page_uri\":\"https://api.twilio.com/2010-04-01/Accounts/AC00000000000000000000000000000000/Messages.json?From=%2B15555550999&PageToken=next\"}"));
        handler.Responses.Enqueue(Json(HttpStatusCode.OK,
            "{\"messages\":[],\"next_page_uri\":null}"));
        var options = Options.Create(new TwilioOptions
        {
            AccountSid = "AC00000000000000000000000000000000",
            AuthToken = "not-a-secret",
            FromNumber = "+15555550999",
            MessagingServiceSid = "MG00000000000000000000000000000000",
            BaseUrl = "https://twilio-override.invalid/proxy"
        });
        using var client = new TwilioRestClient(options, handler);

        await client.SendAsync("+15555550123", "scheduled content", new DateTimeOffset(2026, 8, 31, 10, 0, 0, TimeSpan.Zero), default);
        await client.CancelAsync("SM00000000000000000000000000000001", default);
        await client.RedactAsync("SM00000000000000000000000000000001", default);
        var listed = await client.ListAsync(
            new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 28, 23, 59, 59, TimeSpan.Zero),
            default);

        Assert.AreEqual(1, listed.Count);
        Assert.IsTrue(handler.Requests.All(x => x.Uri.Host == "twilio-override.invalid"));
        Assert.IsTrue(handler.Requests.All(x => x.Uri.AbsolutePath.StartsWith("/proxy/2010-04-01/Accounts/", StringComparison.Ordinal)));
        Assert.IsTrue(handler.Requests.All(x => x.AuthorizationScheme == "Basic"));
        Assert.IsTrue(handler.Requests[0].Body!.Contains("To=%2B15555550123", StringComparison.Ordinal));
        Assert.IsTrue(handler.Requests[0].Body!.Contains("From=%2B15555550999", StringComparison.Ordinal));
        Assert.IsTrue(handler.Requests[0].Body!.Contains("MessagingServiceSid=MG", StringComparison.Ordinal));
        Assert.IsTrue(handler.Requests[0].Body!.Contains("ScheduleType=fixed", StringComparison.Ordinal));
        Assert.IsTrue(handler.Requests[0].Body!.Contains("SendAt=", StringComparison.Ordinal));
        Assert.AreEqual("Status=canceled", handler.Requests[1].Body);
        Assert.AreEqual("Body=", handler.Requests[2].Body);
        Assert.IsTrue(handler.Requests[3].Uri.Query.Contains("From=%2B15555550999", StringComparison.Ordinal));
        Assert.IsTrue(handler.Requests[3].Uri.Query.Contains("DateSent%3E=", StringComparison.Ordinal));
        Assert.IsTrue(handler.Requests[3].Uri.Query.Contains("DateSent%3C=", StringComparison.Ordinal));
        Assert.AreEqual(5, handler.Requests.Count, "Every provider page must be requested.");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Queue<HttpResponseMessage> Responses { get; } = new();
        public List<RequestRecord> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RequestRecord(
                request.RequestUri!,
                request.Headers.Authorization?.Scheme,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return Responses.Dequeue();
        }
    }

    private sealed record RequestRecord(Uri Uri, string? AuthorizationScheme, string? Body);
}
