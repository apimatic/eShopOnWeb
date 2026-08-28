#nullable enable
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
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Twilio;

public class TwilioContractClientTests
{
    [Fact]
    public async Task ScheduledSendUsesV2010FormContractAndMessagingBaseOverride()
    {
        var handler = new RecordingHandler(JsonMessage("SM00000000000000000000000000000001", "scheduled"));
        var options = Options.Create(new TwilioOptions
        {
            AccountSid = "AC00000000000000000000000000000001",
            AuthToken = "not-a-real-secret",
            FromNumber = "+15555550101",
            MessagingServiceSid = "MG00000000000000000000000000000001",
            BaseUrl = "https://messaging-override.invalid"
        });
        var client = new TwilioMessagingClient(new HttpClient(handler), options);
        var sendAt = DateTimeOffset.Parse("2030-01-04T12:30:00Z");

        var result = await client.SendAsync("+15555550102", "scheduled body", sendAt);

        Assert.Equal("scheduled", result.Status);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("messaging-override.invalid", request.Uri.Host);
        Assert.Equal("/2010-04-01/Accounts/AC00000000000000000000000000000001/Messages.json",
            request.Uri.AbsolutePath);
        Assert.Contains("To=%2B15555550102", request.Body);
        Assert.Contains("From=%2B15555550101", request.Body);
        Assert.Contains("MessagingServiceSid=MG00000000000000000000000000000001", request.Body);
        Assert.Contains("ScheduleType=fixed", request.Body);
        Assert.Contains("SendAt=2030-01-04T12%3A30%3A00", request.Body);
    }

    [Fact]
    public async Task ReconciliationSendsFromFilterToProviderAndTraversesEveryPageOnOverride()
    {
        var next = "/2010-04-01/Accounts/AC00000000000000000000000000000001/Messages.json?PageToken=next";
        var firstPage = "{\"messages\":[" +
            JsonMessage("SM00000000000000000000000000000001", "delivered", "Fri, 01 Mar 2030 10:00:00 +0000") +
            "],\"next_page_uri\":\"" + next + "\"}";
        var secondPage = "{\"messages\":[" +
            JsonMessage("SM00000000000000000000000000000002", "undelivered", "Fri, 01 Mar 2030 11:00:00 +0000") +
            "],\"next_page_uri\":null}";
        var handler = new RecordingHandler(firstPage, secondPage);
        var options = Options.Create(new TwilioOptions
        {
            AccountSid = "AC00000000000000000000000000000001",
            AuthToken = "not-a-real-secret",
            FromNumber = "+15555550101",
            MessagingServiceSid = "MG00000000000000000000000000000001",
            BaseUrl = "https://messaging-override.invalid"
        });
        var client = new TwilioMessagingClient(new HttpClient(handler), options);

        var messages = await client.ListAsync(DateTimeOffset.Parse("2030-03-01T00:00:00Z"),
            DateTimeOffset.Parse("2030-03-02T00:00:00Z"));

        Assert.Equal(2, messages.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, x => Assert.Equal("messaging-override.invalid", x.Uri.Host));
        var initialQuery = handler.Requests[0].Uri.Query;
        Assert.Contains("From=%2B15555550101", initialQuery);
        Assert.Contains("DateSent%3E=", initialQuery);
        Assert.Contains("DateSent%3C=", initialQuery);
        Assert.Contains("PageSize=1000", initialQuery);
        Assert.Contains("PageToken=next", handler.Requests[1].Uri.Query);
    }

    [Fact]
    public async Task LookupUsesV2ContractAndReturnsProviderCanonicalNumber()
    {
        var handler = new RecordingHandler(
            "{\"phone_number\":\"+15555550103\",\"valid\":true,\"validation_errors\":null}");
        var options = Options.Create(new TwilioOptions
        {
            AccountSid = "AC00000000000000000000000000000001",
            AuthToken = "not-a-real-secret"
        });
        var validator = new TwilioPhoneNumberValidator(new HttpClient(handler), options);

        var result = await validator.ValidateAsync("(555) 555-0103");

        Assert.True(result.IsValid);
        Assert.Equal("+15555550103", result.CanonicalNumber);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("lookups.twilio.com", request.Uri.Host);
        Assert.StartsWith("/v2/PhoneNumbers/", request.Uri.AbsolutePath);
    }

    private static string JsonMessage(string sid, string status, string? dateSent = null) =>
        $"{{\"sid\":\"{sid}\",\"status\":\"{status}\",\"error_code\":null," +
        $"\"date_created\":\"Fri, 01 Mar 2030 09:00:00 +0000\"," +
        $"\"date_sent\":{(dateSent is null ? "null" : $"\"{dateSent}\"")}}}";

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;
        public List<RecordedRequest> Requests { get; } = new();

        public RecordingHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body);
}
