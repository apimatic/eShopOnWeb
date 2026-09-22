using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Net.Http;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    private static WebApplicationFactory<Program> _application = new TwilioConfiguredApplication();

    public static HttpClient NewClient
    {
        get
        {
            return _application.CreateClient();
        }
    }

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
        _application = new TwilioConfiguredApplication();
    }

    /// <summary>
    /// The host validates Twilio credentials at startup (ValidateOnStart). Supply non-blank
    /// PLACEHOLDER config so the test host boots; these tests never make live Twilio calls.
    /// </summary>
    private sealed class TwilioConfiguredApplication : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Twilio:AccountSid"] = "AC00000000000000000000000000000000",
                    ["Twilio:AuthToken"] = "placeholder-auth-token",
                    ["Twilio:FromNumber"] = "+15005550006",
                    ["Twilio:MessagingServiceSid"] = "MG00000000000000000000000000000000"
                });
            });
        }
    }
}
