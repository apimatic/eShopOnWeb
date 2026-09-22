#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Sms;
using Microsoft.Extensions.Options;
using NSubstitute;
using TwilioSdk;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Sms;

/// <summary>
/// Tests the Twilio gateway through the SDK's own testing seam — a fake <see cref="HttpMessageHandler"/>
/// behind the <see cref="HttpClient"/> the client is constructed with — so no real network call happens.
/// </summary>
public class TwilioMessagingGatewayTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> Bodies { get; } = new();
        public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];
        public string? LastBody => Bodies.Count == 0 ? null : Bodies[^1];

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? null : request.Content.ReadAsStringAsync().Result);
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static TwilioMessagingGateway CreateGateway(StubHandler handler)
    {
        var client = new TwilioSdkClient(new HttpClient(handler), new TwilioSdkClientOptions());
        var options = Options.Create(new TwilioOptions
        {
            AccountSid = "AC00000000000000000000000000000000",
            AuthToken = "token",
            FromNumber = "+15005550006",
            MessagingServiceSid = "MG00000000000000000000000000000000"
        });
        return new TwilioMessagingGateway(client, options, Substitute.For<IAppLogger<TwilioMessagingGateway>>());
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task SendAsync_ReturnsProviderSidAndStatus_AndPostsToMessages()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created,
            """{ "sid": "SM123", "status": "queued", "to": "+15551234567", "from": "+15005550006" }"""));
        var gateway = CreateGateway(handler);

        var result = await gateway.SendAsync("+15551234567", "hello", CancellationToken.None);

        Assert.Equal("SM123", result.Sid);
        Assert.Equal("queued", result.Status);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("/Messages.json", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("Body=hello", handler.LastBody);
        Assert.Contains("To=", handler.LastBody);
    }

    [Fact]
    public async Task SendAsync_TranslatesProviderErrorToSmsGatewayException_WithStatus()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.BadRequest,
            """{ "code": 21211, "message": "invalid", "status": 400 }"""));
        var gateway = CreateGateway(handler);

        var ex = await Assert.ThrowsAsync<SmsGatewayException>(
            () => gateway.SendAsync("+15551234567", "hello", CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task RedactContentAsync_PostsEmptyBodyToTheMessageResource()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{ "sid": "SM123", "status": "delivered" }"""));
        var gateway = CreateGateway(handler);

        await gateway.RedactContentAsync("SM123", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("/Messages/SM123.json", handler.LastRequest!.RequestUri!.AbsolutePath);
        // The empty (non-null) Body is what redacts the text at the provider.
        Assert.Contains("Body=", handler.LastBody);
    }

    [Fact]
    public async Task ValidateAsync_ReportsInvalidNumber()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{ "valid": false, "phone_number": "+1555", "country_code": "US" }"""));
        var gateway = CreateGateway(handler);

        var result = await gateway.ValidateAsync("+1555", CancellationToken.None);

        Assert.False(result.IsValid);
    }
}
