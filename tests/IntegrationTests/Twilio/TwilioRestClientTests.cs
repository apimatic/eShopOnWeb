using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Twilio;

public class TwilioRestClientTests
{
    private static readonly TwilioOptions Settings = new()
    {
        AccountSid = "ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        AuthToken = "secret",
        FromNumber = "+15550000001",
        MessagingServiceSid = "MGaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        BaseUrl = "https://messaging.example.test/twilio-root"
    };

    [Fact]
    public async Task ScheduledSendUsesMessagingContractAndBaseUrlOverride()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Created,
            "{\"sid\":\"SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"status\":\"scheduled\"}"));
        using var client = new TwilioRestClient(Options.Create(Settings), handler);
        var sendAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        var result = await client.SendAsync("+15550000002", "follow up", sendAt);

        Assert.Equal("scheduled", result.Status);
        var request = Assert.Single(handler.Requests);
        Assert.StartsWith("https://messaging.example.test/twilio-root/2010-04-01/Accounts/", request.Uri);
        Assert.Contains("To=%2B15550000002", request.Body);
        Assert.Contains("From=%2B15550000001", request.Body);
        Assert.Contains("MessagingServiceSid=MGaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", request.Body);
        Assert.Contains("ScheduleType=fixed", request.Body);
        Assert.Contains("SendAt=2026-09-01T12%3A00%3A00.0000000Z", request.Body);
        Assert.StartsWith("Basic ", request.Authorization);
    }

    [Fact]
    public async Task ReconciliationFiltersByConfiguredSenderAndFollowsEveryPageOnOverrideHost()
    {
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            Json(HttpStatusCode.OK, "{\"messages\":[{\"sid\":\"SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"status\":\"delivered\"}],\"next_page_uri\":\"/2010-04-01/Accounts/ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/Messages.json?PageToken=next\"}"),
            Json(HttpStatusCode.OK, "{\"messages\":[{\"sid\":\"SMbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"status\":\"undelivered\"}],\"next_page_uri\":null}")
        });
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var client = new TwilioRestClient(Options.Create(Settings), handler);

        var result = await client.ListAsync(DateTimeOffset.Parse("2026-08-28T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-29T00:00:00Z"));

        Assert.Equal(2, result.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
            Assert.StartsWith("https://messaging.example.test/twilio-root/", request.Uri));
        Assert.Contains("From=%2B15550000001", handler.Requests[0].Uri);
        Assert.Contains("DateSent%3E=", handler.Requests[0].Uri);
        Assert.Contains("DateSent%3C=", handler.Requests[0].Uri);
        Assert.Contains("PageSize=1000", handler.Requests[0].Uri);
        Assert.Contains("PageToken=next", handler.Requests[1].Uri);
    }

    [Fact]
    public async Task UpdateOperationsUseSpecFormsAndLookupIgnoresMessagingOverride()
    {
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            Json(HttpStatusCode.OK, "{\"sid\":\"SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"status\":\"canceled\"}"),
            Json(HttpStatusCode.OK, "{\"sid\":\"SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"status\":\"delivered\",\"body\":\"\"}"),
            Json(HttpStatusCode.OK, "{\"phone_number\":\"+15550000002\",\"valid\":true}")
        });
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var client = new TwilioRestClient(Options.Create(Settings), handler);

        await client.CancelAsync("SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        await client.RedactContentAsync("SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var lookup = await client.ValidateAsync("+1 555 000 0002");

        Assert.Contains("Status=canceled", handler.Requests[0].Body);
        Assert.Equal("Body=", handler.Requests[1].Body);
        Assert.StartsWith("https://lookups.twilio.com/v2/PhoneNumbers/", handler.Requests[2].Uri);
        Assert.True(lookup.IsValid);
        Assert.Equal("+15550000002", lookup.CanonicalNumber);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string value) => new(status)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;
        public List<RecordedRequest> Requests { get; } = new();

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(request.RequestUri!.AbsoluteUri,
                request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.ToString() ?? string.Empty));
            return _response(request);
        }
    }

    private sealed record RecordedRequest(string Uri, string Body, string Authorization);
}
