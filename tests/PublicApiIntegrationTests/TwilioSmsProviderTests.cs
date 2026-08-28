using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests;

[TestClass]
public class TwilioSmsProviderTests
{
    [TestMethod]
    public async Task ScheduledSendUsesOverrideAndRequiredTwilioFormFields()
    {
        var handler = new RecordingHandler(_ => MessageResponse(HttpStatusCode.Created, "scheduled"));
        using var provider = new TwilioSmsProvider(Options(), handler);
        const string destination = "authorized-test-destination";

        await provider.SendMessageAsync(
            destination,
            "follow up",
            DateTimeOffset.Parse("2026-09-01T12:00:00Z"),
            default);

        var request = handler.Requests.Single();
        Assert.AreEqual("https://messaging.test/custom-root/2010-04-01/Accounts/AC00000000000000000000000000000000/Messages.json", request.Uri);
        StringAssert.Contains(request.Body!, "To=authorized-test-destination");
        StringAssert.Contains(request.Body!, "From=configured-sender");
        StringAssert.Contains(request.Body!, "MessagingServiceSid=MG00000000000000000000000000000000");
        StringAssert.Contains(request.Body!, "ScheduleType=fixed");
        StringAssert.Contains(request.Body!, "SendAt=");
    }

    [TestMethod]
    public async Task LookupAlwaysUsesLookupHostAndReturnsCanonicalNumber()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            "{\"phone_number\":\"canonical-test-destination\",\"valid\":true,\"validation_errors\":[]}"));
        using var provider = new TwilioSmsProvider(Options(), handler);

        var result = await provider.ValidatePhoneNumberAsync("raw-test-destination", null, default);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual("canonical-test-destination", result.E164Number);
        StringAssert.StartsWith(handler.Requests.Single().Uri, "https://lookups.twilio.com/v2/PhoneNumbers/");
    }

    [TestMethod]
    public async Task ReconciliationFiltersByConfiguredFromAndFollowsEveryPageOnOverride()
    {
        var call = 0;
        var handler = new RecordingHandler(_ => ++call == 1
            ? Json(HttpStatusCode.OK,
                "{\"messages\":[{" + MessageFields("SM00000000000000000000000000000001") + "}]," +
                "\"next_page_uri\":\"https://api.twilio.com/2010-04-01/Accounts/AC00000000000000000000000000000000/Messages.json?PageToken=next\"}")
            : Json(HttpStatusCode.OK,
                "{\"messages\":[{" + MessageFields("SM00000000000000000000000000000002") + "}],\"next_page_uri\":null}"));
        using var provider = new TwilioSmsProvider(Options(), handler);

        var messages = await provider.ListMessagesAsync(
            DateTimeOffset.Parse("2026-08-27T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-29T00:00:00Z"),
            default);

        Assert.AreEqual(2, messages.Count);
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsTrue(handler.Requests.All(x => x.Uri.StartsWith("https://messaging.test/custom-root/", StringComparison.Ordinal)));
        StringAssert.Contains(handler.Requests[0].Uri, "From=configured-sender");
        StringAssert.Contains(handler.Requests[0].Uri, "DateSent%3E=2026-08-26");
        StringAssert.Contains(handler.Requests[0].Uri, "DateSent%3C=2026-08-30");
        StringAssert.Contains(handler.Requests[1].Uri, "PageToken=next");
    }

    [TestMethod]
    public async Task RedactionSendsEmptyBodyAndRequiresProviderConfirmation()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            "{" + MessageFields("SM00000000000000000000000000000001") + ",\"body\":\"\"}"));
        using var provider = new TwilioSmsProvider(Options(), handler);

        await provider.RedactMessageAsync("SM00000000000000000000000000000001", default);

        Assert.AreEqual("Body=", handler.Requests.Single().Body);
    }

    private static TwilioOptions Options() => new()
    {
        AccountSid = "AC00000000000000000000000000000000",
        AuthToken = new string('x', 32),
        FromNumber = "configured-sender",
        MessagingServiceSid = "MG00000000000000000000000000000000",
        BaseUrl = "https://messaging.test/custom-root"
    };

    private static HttpResponseMessage MessageResponse(HttpStatusCode status, string messageStatus)
    {
        return Json(status, "{" + MessageFields("SM00000000000000000000000000000001", messageStatus) + "}");
    }

    private static string MessageFields(string sid, string status = "delivered")
    {
        return $"\"sid\":\"{sid}\",\"status\":\"{status}\",\"error_code\":null," +
               "\"date_created\":\"Fri, 28 Aug 2026 10:00:00 +0000\"," +
               "\"date_updated\":\"Fri, 28 Aug 2026 10:00:01 +0000\"," +
               "\"date_sent\":\"Fri, 28 Aug 2026 10:00:01 +0000\"";
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<RecordedRequest, HttpResponseMessage> _response;

        public RecordingHandler(Func<RecordedRequest, HttpResponseMessage> response)
        {
            _response = response;
        }

        public List<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var recorded = new RecordedRequest(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(recorded);
            return _response(recorded);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string? Body);
}
