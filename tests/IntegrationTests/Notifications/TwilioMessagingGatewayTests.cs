using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Notifications;

public class TwilioMessagingGatewayTests
{
    [Fact]
    public async Task ValidationReturnsProviderCanonicalValue()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"phone_number":"canonical-destination","valid":true}"""));
        var gateway = Gateway(handler);

        var result = await gateway.ValidateAndCanonicalizeAsync("typed-destination", default);

        Assert.Equal("canonical-destination", result);
        Assert.Single(handler.Requests);
        Assert.Contains("/v2/PhoneNumbers/typed-destination", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ScheduledSendUsesProviderSchedulingAndConfiguredSender()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created,
            """{"sid":"SM00000000000000000000000000000000","status":"scheduled"}"""));
        var gateway = Gateway(handler);

        var result = await gateway.SendAsync("test-destination", "test-content",
            DateTimeOffset.UtcNow.AddDays(3), default);

        Assert.Equal("scheduled", result.Status);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Contains("To=test-destination", handler.Bodies[0]);
        Assert.Contains("From=configured-sender", handler.Bodies[0]);
        Assert.Contains("MessagingServiceSid=configured-service", handler.Bodies[0]);
        Assert.Contains("ScheduleType=fixed", handler.Bodies[0]);
        Assert.Contains("Body=test-content", handler.Bodies[0]);
    }

    [Fact]
    public async Task ReconciliationQueryFiltersAtProviderByConfiguredSender()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"messages":[],"page":0,"page_size":1000,"next_page_uri":null}"""));
        var gateway = Gateway(handler);

        await gateway.ListAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, default);

        Assert.Single(handler.Requests);
        Assert.Contains("From=configured-sender", handler.Requests[0].RequestUri!.Query);
    }

    private static TwilioMessagingGateway Gateway(StubHandler handler)
    {
        var settings = new TwilioSettings
        {
            AccountSid = "test-account",
            AuthToken = "test-token",
            FromNumber = "configured-sender",
            MessagingServiceSid = "configured-service"
        };
        var options = new TwilioSdkClientOptions
        {
            Retry = RetryOptions.Disabled(),
            Logging = new LoggingOptions { LoggerFactory = NullLoggerFactory.Instance },
            AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = settings.AccountSid,
                Password = settings.AuthToken
            }
        };
        return new TwilioMessagingGateway(new TwilioSdkClient(new HttpClient(handler), options), settings);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            var response = _response(request);
            response.RequestMessage = request;
            return response;
        }
    }
}
