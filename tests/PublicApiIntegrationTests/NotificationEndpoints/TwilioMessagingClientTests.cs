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

namespace PublicApiIntegrationTests.NotificationEndpoints;

[TestClass]
public class TwilioMessagingClientTests
{
    [TestMethod]
    public async Task ScheduledSendUsesOverrideSpecificSenderAndMessagingService()
    {
        var handler = new RecordingHandler((_, _) => MessageResponse("scheduled"));
        using var client = new TwilioMessagingClient(Options.Create(CreateOptions()), handler);
        var sendAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        var result = await client.SendAsync("+10000000000", "body", sendAt, CancellationToken.None);

        Assert.AreEqual("scheduled", result.Status);
        var request = handler.Requests.Single();
        Assert.IsTrue(request.Url.StartsWith("https://override.invalid/twilio-root/2010-04-01/Accounts/"));
        var form = ParseForm(request.Body!);
        Assert.AreEqual("+19999999999", form["From"]);
        Assert.AreEqual("+10000000000", form["To"]);
        Assert.AreEqual("MG00000000000000000000000000000000", form["MessagingServiceSid"]);
        Assert.AreEqual("fixed", form["ScheduleType"]);
        Assert.AreEqual("2026-09-01T12:00:00Z", form["SendAt"]);
    }

    [TestMethod]
    public async Task ReconciliationUsesOverrideAndServerSideFromFilterOnEveryPage()
    {
        var call = 0;
        var handler = new RecordingHandler((request, _) =>
        {
            call++;
            return call == 1
                ? JsonResponse("""
                    {"messages":[{"sid":"SM00000000000000000000000000000001","status":"delivered","date_created":"Fri, 28 Aug 2026 10:00:00 +0000","date_sent":"Fri, 28 Aug 2026 10:00:01 +0000"}],"next_page_uri":"https://api.twilio.com/2010-04-01/Accounts/AC00000000000000000000000000000000/Messages.json?From=%2B19999999999&PageToken=next"}
                    """)
                : JsonResponse("""
                    {"messages":[{"sid":"SM00000000000000000000000000000002","status":"undelivered","error_code":30034,"date_created":"Fri, 28 Aug 2026 10:01:00 +0000","date_sent":"Fri, 28 Aug 2026 10:01:01 +0000"}],"next_page_uri":null}
                    """);
        });
        using var client = new TwilioMessagingClient(Options.Create(CreateOptions()), handler);

        var result = await client.ListMessagesAsync(
            new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 28, 11, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsTrue(handler.Requests.All(x => x.Url.StartsWith("https://override.invalid/twilio-root/")));
        Assert.IsTrue(handler.Requests.All(x => Uri.UnescapeDataString(x.Url).Contains("From=+19999999999")));
    }

    [TestMethod]
    public async Task LookupUsesLookupHostInsteadOfMessagingOverride()
    {
        var handler = new RecordingHandler((_, _) => JsonResponse(
            "{\"valid\":true,\"phone_number\":\"+10000000000\"}"));
        using var client = new TwilioMessagingClient(Options.Create(CreateOptions()), handler);

        var result = await client.ValidatePhoneNumberAsync("typed", CancellationToken.None);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual("+10000000000", result.CanonicalNumber);
        Assert.IsTrue(handler.Requests.Single().Url.StartsWith("https://lookups.twilio.com/v2/PhoneNumbers/"));
    }

    private static TwilioOptions CreateOptions() => new()
    {
        AccountSid = "AC00000000000000000000000000000000",
        AuthToken = "not-a-real-secret",
        FromNumber = "+19999999999",
        MessagingServiceSid = "MG00000000000000000000000000000000",
        BaseUrl = "https://override.invalid/twilio-root"
    };

    private static HttpResponseMessage MessageResponse(string status) => JsonResponse(
        $"{{\"sid\":\"SM00000000000000000000000000000000\",\"status\":\"{status}\",\"date_created\":\"Fri, 28 Aug 2026 10:00:00 +0000\",\"date_sent\":null}}");

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static Dictionary<string, string> ParseForm(string body) => body.Split('&')
        .Select(x => x.Split('=', 2))
        .ToDictionary(x => WebUtility.UrlDecode(x[0]), x => WebUtility.UrlDecode(x[1]));
}

internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _response;

    public RecordingHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> response)
    {
        _response = response;
    }

    public List<RecordedRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.ToString(), body));
        return _response(request, cancellationToken);
    }
}

internal sealed record RecordedRequest(HttpMethod Method, string Url, string? Body);
