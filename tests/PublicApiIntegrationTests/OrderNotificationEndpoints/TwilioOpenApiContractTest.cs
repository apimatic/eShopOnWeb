using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Notifications;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderNotificationEndpoints;

[TestClass]
public class TwilioOpenApiContractTest
{
    private static readonly TwilioOptions Options = new()
    {
        AccountSid = "AC" + new string('a', 32),
        AuthToken = "secret",
        FromNumber = "+15005550006",
        MessagingServiceSid = "MG" + new string('b', 32),
        BaseUrl = "https://messaging-override.example/custom-root"
    };

    [TestMethod]
    public async Task MessagingOverrideFromFilterAndPaginationFollowTheSpec()
    {
        var requests = new List<Uri>();
        var handler = new DelegateHandler(request =>
        {
            requests.Add(request.RequestUri!);
            var next = requests.Count == 1
                ? "\"https://api.twilio.com/2010-04-01/Accounts/ACx/Messages.json?PageToken=next\""
                : "null";
            return Json($$"""
                {"messages":[{"sid":"SM{{new string('1', 32)}}","status":"delivered","from":"+15005550006","to":"+15550000000","body":"test","date_created":"Thu, 27 Aug 2026 12:00:00 +0000","date_sent":"Thu, 27 Aug 2026 12:00:01 +0000"}],"next_page_uri":{{next}}}
                """);
        });
        using var client = new TwilioClient(Microsoft.Extensions.Options.Options.Create(Options), handler,
            new DelegateHandler(new Func<HttpRequestMessage, HttpResponseMessage>(_ =>
                throw new AssertFailedException("Lookup host was unexpectedly used."))));

        var result = await client.ListAsync(DateTimeOffset.Parse("2026-08-27T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-28T00:00:00Z"));

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(2, requests.Count);
        Assert.IsTrue(requests.All(x => x.AbsoluteUri.StartsWith(Options.BaseUrl!, StringComparison.Ordinal)));
        var decodedQuery = Uri.UnescapeDataString(requests[0].Query);
        StringAssert.Contains(decodedQuery, "From=+15005550006");
        StringAssert.Contains(decodedQuery, "DateSent>");
        StringAssert.Contains(decodedQuery, "DateSent<");
    }

    [TestMethod]
    public async Task LookupUsesItsOwnHostAndScheduledSendUsesOpenApiFormFields()
    {
        Uri? lookupUri = null;
        string? form = null;
        var lookupHandler = new DelegateHandler(request =>
        {
            lookupUri = request.RequestUri;
            return Json("""{"phone_number":"+15550000000","country_code":"US","valid":true,"validation_errors":null}""");
        });
        var messageHandler = new DelegateHandler(async request =>
        {
            form = await request.Content!.ReadAsStringAsync();
            return Json($$"""
                {"sid":"SM{{new string('2', 32)}}","status":"scheduled","from":"+15005550006","to":"+15550000000","body":"test","date_created":"Thu, 27 Aug 2026 12:00:00 +0000","date_sent":null}
                """);
        });
        using var client = new TwilioClient(Microsoft.Extensions.Options.Options.Create(Options), messageHandler,
            lookupHandler);

        var lookup = await client.ValidateAsync("5550000000", "US");
        await client.SendAsync(lookup.CanonicalNumber!, "test", DateTimeOffset.UtcNow.AddDays(3));

        Assert.IsTrue(lookup.IsValid);
        Assert.AreEqual("lookups.twilio.com", lookupUri!.Host);
        StringAssert.Contains(form, "To=%2B15550000000");
        StringAssert.Contains(form, "From=%2B15005550006");
        StringAssert.Contains(form, "MessagingServiceSid=MG");
        StringAssert.Contains(form, "ScheduleType=fixed");
        StringAssert.Contains(form, "SendAt=");
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;
        public DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
            _handler = request => Task.FromResult(handler(request));
        public DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => _handler(request);
    }
}
