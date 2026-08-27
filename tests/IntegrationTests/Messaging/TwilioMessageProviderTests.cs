#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Messaging;

public sealed class TwilioMessageProviderTests
{
    [Fact]
    public async Task KeepsLookupOnItsProviderHostAndSendsReconciliationFilterOnEveryPage()
    {
        var requests = new List<ObservedRequest>();
        var handler = new StubHandler(async request =>
        {
            requests.Add(new ObservedRequest(
                request.Method,
                request.RequestUri!,
                request.Content is null ? null : await request.Content.ReadAsStringAsync()));

            if (request.RequestUri!.Host == "lookups.twilio.com")
            {
                return Json("""{"valid":true,"phone_number":"+16045550123"}""");
            }

            bool secondPage = request.RequestUri.Query.Contains("PageToken=next-token", StringComparison.Ordinal);
            string next = secondPage
                ? "null"
                : "\"/2010-04-01/Accounts/test/Messages.json?PageToken=next-token\"";
            return Json($$"""
                {
                  "messages": [{
                    "sid": "SM{{(secondPage ? "2" : "1")}}",
                    "from": "+15005550006",
                    "status": "delivered",
                    "date_sent": "2026-08-28T10:00:00Z"
                  }],
                  "next_page_uri": {{next}}
                }
                """);
        });
        TwilioMessageProvider provider = CreateProvider(handler, "https://messaging.example.test");

        string canonical = await provider.ValidateAndCanonicalizeAsync("6045550123", CancellationToken.None);
        Assert.Equal("+16045550123", canonical);
        var messages = await provider.ListAsync(
            DateTimeOffset.Parse("2026-08-28T09:00:00Z"),
            DateTimeOffset.Parse("2026-08-28T11:00:00Z"),
            CancellationToken.None);

        Assert.Equal(2, messages.Count);
        Assert.Equal("lookups.twilio.com", requests[0].Uri.Host);
        List<ObservedRequest> listRequests = requests.Where(x => x.Uri.Host == "messaging.example.test").ToList();
        Assert.Equal(2, listRequests.Count);
        Assert.All(listRequests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("From=%2B15005550006", request.Uri.Query, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DateSent%3C", request.Uri.Query, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DateSent%3E", request.Uri.Query, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task ImmediateSendUsesConfiguredFromAndOneProviderPost()
    {
        var requests = new List<ObservedRequest>();
        var handler = new StubHandler(async request =>
        {
            requests.Add(new ObservedRequest(
                request.Method,
                request.RequestUri!,
                request.Content is null ? null : await request.Content.ReadAsStringAsync()));
            return Json("""{"sid":"SM123","from":"+15005550006","status":"queued"}""");
        });
        TwilioMessageProvider provider = CreateProvider(handler, "https://messaging.example.test");

        await provider.SendAsync("+16045550123", "test message", CancellationToken.None);

        ObservedRequest request = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("messaging.example.test", request.Uri.Host);
        Assert.Contains("From=%2B15005550006", request.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("To=%2B16045550123", request.Body, StringComparison.OrdinalIgnoreCase);
    }

    private static TwilioMessageProvider CreateProvider(HttpMessageHandler handler, string messagingBaseUrl)
    {
        var options = new TwilioSdkClientOptions
        {
            Environment = ServerEnvironment.Production,
            AccountSidAuthToken = new BasicAuthCredentials { Username = "test", Password = "test" }
        };
        options.Server.Default.Production.BaseUrl = messagingBaseUrl;
        var client = new TwilioSdkClient(new HttpClient(handler), options);
        var settings = Options.Create(new TwilioSettings
        {
            AccountSid = "test",
            AuthToken = "test",
            FromNumber = "+15005550006",
            MessagingServiceSid = "MGtest",
            BaseUrl = messagingBaseUrl
        });
        return new TwilioMessageProvider(client, settings, new ProviderWriteGuard());
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed record ObservedRequest(HttpMethod Method, Uri Uri, string? Body);

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request);
    }
}
