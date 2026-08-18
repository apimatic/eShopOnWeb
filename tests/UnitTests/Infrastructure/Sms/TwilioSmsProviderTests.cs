using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Sms;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Sms;

/// <summary>
/// Tests the Twilio integration at its real seam — the <see cref="HttpClient"/> the SDK client is built over —
/// so no network call happens. Asserts response mapping and the error-translation boundary.
/// </summary>
public class TwilioSmsProviderTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;

        public StubHandler(HttpStatusCode status, string json)
        {
            _status = status;
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
    }

    private static TwilioSmsProvider Build(HttpStatusCode status, string json)
    {
        var options = new TwilioSdkClientOptions
        {
            Environment = ServerEnvironment.Production,
            AccountSidAuthToken = new BasicAuthCredentials { Username = "ACtest", Password = "token" }
        };
        var client = new TwilioSdkClient(new HttpClient(new StubHandler(status, json)), options);
        var settings = new TwilioSettings
        {
            AccountSid = "ACtest",
            AuthToken = "token",
            FromNumber = "+15550000000",
            MessagingServiceSid = "MGtest",
            RequestTimeoutSeconds = 5
        };
        return new TwilioSmsProvider(client, settings);
    }

    [Fact]
    public async Task SendAsync_ReturnsSidAndStatus_FromProviderResponse()
    {
        var provider = Build(HttpStatusCode.Created,
            "{\"sid\":\"SM123\",\"status\":\"queued\",\"to\":\"+15551234567\",\"from\":\"+15550000000\"}");

        var result = await provider.SendAsync("+15551234567", "hello", CancellationToken.None);

        Assert.Equal("SM123", result.Sid);
        Assert.Equal("queued", result.Status);
    }

    [Fact]
    public async Task ValidateNumberAsync_ReturnsCanonicalE164_WhenValid()
    {
        var provider = Build(HttpStatusCode.OK,
            "{\"phone_number\":\"+15551234567\",\"valid\":true}");

        var result = await provider.ValidateNumberAsync("(555) 123-4567", CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("+15551234567", result.CanonicalNumber);
    }

    [Fact]
    public async Task ValidateNumberAsync_RejectsWhenProviderSaysInvalid()
    {
        var provider = Build(HttpStatusCode.OK,
            "{\"valid\":false,\"validation_errors\":[\"TOO_SHORT\"]}");

        var result = await provider.ValidateNumberAsync("123", CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("TOO_SHORT", result.Reasons);
    }

    [Fact]
    public async Task ValidateNumberAsync_TreatsClientErrorAsNotUsable_NotAnOutage()
    {
        // A 404 (un-parseable number) is a rejection at registration, not a thrown provider failure.
        var provider = Build(HttpStatusCode.NotFound, "{\"code\":20404}");

        var result = await provider.ValidateNumberAsync("garbage", CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task SendAsync_TranslatesProviderErrorToSmsProviderException_CarryingStatus()
    {
        var provider = Build(HttpStatusCode.BadRequest, "{\"code\":21211,\"message\":\"Invalid To number\"}");

        var ex = await Assert.ThrowsAsync<SmsProviderException>(
            () => provider.SendAsync("+1", "hello", CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }
}
