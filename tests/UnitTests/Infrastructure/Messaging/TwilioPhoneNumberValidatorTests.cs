using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TwilioSdk;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Messaging;

public class TwilioPhoneNumberValidatorTests
{
    private static TwilioPhoneNumberValidator Validator(StubHttpMessageHandler handler)
    {
        var settings = new TwilioSettings { AccountSid = "AC", AuthToken = "t", FromNumber = "+1", MessagingServiceSid = "MG", CallTimeoutSeconds = 30 };
        var client = new TwilioSdkClient(new HttpClient(handler), new TwilioSdkClientOptions());
        var logger = Substitute.For<IAppLogger<TwilioPhoneNumberValidator>>();
        return new TwilioPhoneNumberValidator(client, Options.Create(settings), logger);
    }

    [Fact]
    public async Task ValidNumber_ReturnsUsableWithCanonicalForm()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK,
            """{ "valid": true, "phone_number": "+15145550123", "country_code": "CA" }"""));

        var result = await Validator(handler).ValidateAsync("(514) 555-0123", CancellationToken.None);

        Assert.True(result.IsUsable);
        Assert.Equal("+15145550123", result.CanonicalE164);   // provider's canonical form, not the caller input
    }

    [Fact]
    public async Task InvalidNumber_ReturnsNotUsable()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK,
            """{ "valid": false, "phone_number": null }"""));

        var result = await Validator(handler).ValidateAsync("nonsense", CancellationToken.None);

        Assert.False(result.IsUsable);
        Assert.Null(result.CanonicalE164);
    }

    [Fact]
    public async Task NotFound_IsTreatedAsUnusableNumber_NotAFault()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.NotFound,
            """{ "code": 20404, "message": "not found" }"""));

        var result = await Validator(handler).ValidateAsync("+1", CancellationToken.None);

        Assert.False(result.IsUsable);
    }
}
