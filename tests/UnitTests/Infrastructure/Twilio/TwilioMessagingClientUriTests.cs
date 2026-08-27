using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Twilio;

public class TwilioMessagingClientUriTests
{
    [Fact]
    public void UsesConfiguredBaseUrlVerbatimIncludingPathPrefix()
    {
        var settings = new TwilioSettings
        {
            AccountSid = "ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            BaseUrl = "https://proxy.example.test/twilio"
        };
        var client = new TwilioMessagingClient(new System.Net.Http.HttpClient(), Options.Create(settings));

        var uri = client.CreateRequestUri("2010-04-01/Accounts/ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/Messages.json");

        Assert.Equal("https://proxy.example.test/twilio/2010-04-01/Accounts/ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/Messages.json", uri.ToString());
    }

    [Fact]
    public void DefaultsToTwilioMessagingHost()
    {
        var settings = new TwilioSettings
        {
            AccountSid = "ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        };
        var client = new TwilioMessagingClient(new System.Net.Http.HttpClient(), Options.Create(settings));

        var uri = client.CreateRequestUri("2010-04-01/Accounts/ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/Messages.json");

        Assert.Equal("https://api.twilio.com/2010-04-01/Accounts/ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/Messages.json", uri.ToString());
    }
}
