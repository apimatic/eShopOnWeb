using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Options;
using NSubstitute;
using TwilioSdk;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Twilio;

/// <summary>
/// Tests the Twilio gateway through the SDK's own test seam — a fake <see cref="HttpMessageHandler"/> — so no
/// real network call is made. These exercise the mapping and error-boundary behaviour the live flow relies on.
/// </summary>
public class TwilioSmsGatewayTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static TwilioSmsGateway BuildGateway(StubHandler handler)
    {
        var client = new TwilioSdkClient(new HttpClient(handler), new TwilioSdkClientOptions());
        var options = Options.Create(new TwilioOptions
        {
            AccountSid = "AC00000000000000000000000000000000",
            AuthToken = "token",
            FromNumber = "+15005550006",
            MessagingServiceSid = "MG00000000000000000000000000000000",
            FollowUpDelayDays = 3
        });
        return new TwilioSmsGateway(client, options, Substitute.For<IAppLogger<TwilioSmsGateway>>());
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task SendAsync_PostsToMessagesEndpoint_AndReturnsProviderState()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created,
            """{"sid":"SM123","status":"queued","from":"+15005550006","to":"+15551234567","date_sent":null}"""));
        var gateway = BuildGateway(handler);

        var result = await gateway.SendAsync("+15551234567", "hello", CancellationToken.None);

        Assert.Equal("SM123", result.Sid);
        Assert.Equal("queued", result.Status);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Contains("/Messages.json", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ValidateNumberAsync_MapsProvider404_ToInvalid()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, """{"code":20404}"""));
        var gateway = BuildGateway(handler);

        var result = await gateway.ValidateNumberAsync("not-a-number", CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Null(result.CanonicalNumber);
    }

    [Fact]
    public async Task ValidateNumberAsync_ReturnsCanonicalNumber_WhenValid()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"valid":true,"phone_number":"+15551234567"}"""));
        var gateway = BuildGateway(handler);

        var result = await gateway.ValidateNumberAsync("(555) 123-4567", CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("+15551234567", result.CanonicalNumber);
    }

    [Fact]
    public async Task SendAsync_TranslatesProviderError_ToSmsGatewayException()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.InternalServerError, """{"code":30000}"""));
        var gateway = BuildGateway(handler);

        var ex = await Assert.ThrowsAsync<SmsGatewayException>(
            () => gateway.SendAsync("+15551234567", "hello", CancellationToken.None));
        Assert.Equal(500, ex.StatusCode);
    }
}
