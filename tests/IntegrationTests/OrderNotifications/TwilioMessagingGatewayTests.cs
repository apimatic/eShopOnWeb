using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.OrderNotifications;

public sealed class TwilioMessagingGatewayTests
{
    [Fact]
    public async Task MessagingOverrideSchedulingAndSenderConstrainedListAreOnTheWire()
    {
        var handler = new StubHandler(request =>
        {
            var json = request.RequestUri!.AbsolutePath.EndsWith("Messages.json", StringComparison.Ordinal) && request.Method == HttpMethod.Get
                ? "{\"messages\":[{\"sid\":\"SM-list\",\"status\":\"delivered\"}],\"next_page_uri\":null}"
                : "{\"sid\":\"SM-write\",\"status\":\"scheduled\",\"from\":null}";
            return Json(json);
        });
        var gateway = Gateway(handler);

        await gateway.ScheduleAsync("destination-fixture", "body", DateTimeOffset.UtcNow.AddDays(3), CancellationToken.None);
        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow.AddDays(1);
        var listed = await gateway.ListAsync(from, to, CancellationToken.None);

        Assert.Equal("SM-list", Assert.Single(listed).Sid);
        Assert.All(handler.Requests, request => Assert.Equal("messaging.test", request.Uri.Host));
        var scheduleBody = handler.Requests[0].Body!;
        Assert.Contains("ScheduleType=fixed", scheduleBody);
        Assert.Contains("MessagingServiceSid=service-fixture", scheduleBody);
        Assert.DoesNotContain("From=", scheduleBody);
        Assert.Contains("From=sender-fixture", handler.Requests[1].Uri.Query);
        Assert.Contains("DateSent%3C=", handler.Requests[1].Uri.Query);
        Assert.Contains("DateSent%3E=", handler.Requests[1].Uri.Query);
    }

    [Fact]
    public async Task LookupUsesItsOwnHostAndRedactionSendsAnEmptyBody()
    {
        var handler = new StubHandler(request => request.Method == HttpMethod.Get
            ? Json("{\"phone_number\":\"canonical-fixture\",\"valid\":true}")
            : Json("{\"sid\":\"SM-redact\",\"status\":\"delivered\"}"));
        var gateway = Gateway(handler);

        Assert.Equal("canonical-fixture", await gateway.ValidateAndCanonicalizeAsync("input-fixture", CancellationToken.None));
        await gateway.RedactAsync("SM-redact", CancellationToken.None);

        Assert.Equal("lookups.twilio.com", handler.Requests[0].Uri.Host);
        Assert.Equal("messaging.test", handler.Requests[1].Uri.Host);
        Assert.Contains("Body=", handler.Requests[1].Body!);
    }

    [Fact]
    public async Task ReconciliationFollowsEveryContinuationWithTheSenderFilter()
    {
        var page = 0;
        var handler = new StubHandler(_ => ++page == 1
            ? Json("{\"messages\":[{\"sid\":\"SM-page-1\",\"status\":\"delivered\"}],\"next_page_uri\":\"/Messages.json?PageToken=next-token\"}")
            : Json("{\"messages\":[{\"sid\":\"SM-page-2\",\"status\":\"undelivered\"}],\"next_page_uri\":null}"));
        var gateway = Gateway(handler);

        var messages = await gateway.ListAsync(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1),
            CancellationToken.None);

        Assert.Equal(2, messages.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Contains("From=sender-fixture", request.Uri.Query));
        Assert.Contains("PageToken=next-token", handler.Requests[1].Uri.Query);
    }

    private static TwilioMessagingGateway Gateway(StubHandler handler)
    {
        var options = new TwilioSdkClientOptions
        {
            Environment = ServerEnvironment.Production,
            AccountSidAuthToken = new BasicAuthCredentials { Username = "account-fixture", Password = "token-fixture" }
        };
        options.Server.Default.Production.BaseUrl = "https://messaging.test";
        var client = new TwilioSdkClient(new HttpClient(handler), options);
        var settings = Options.Create(new TwilioSettings
        {
            AccountSid = "account-fixture",
            AuthToken = "token-fixture",
            FromNumber = "sender-fixture",
            MessagingServiceSid = "service-fixture",
            BaseUrl = "https://messaging.test"
        });
        return new TwilioMessagingGateway(client, settings);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body));
            return responder(request);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string Body);
}
