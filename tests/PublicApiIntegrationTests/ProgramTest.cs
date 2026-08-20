using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Net.Http;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    private static WebApplicationFactory<Program> _application = new();

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
        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("UseOnlyInMemoryDatabase", "true");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["UseOnlyInMemoryDatabase"] = "true",
                    ["Twilio:AccountSid"] = "ACtestaccountsidnotarealsecret0000",
                    ["Twilio:AuthToken"] = "test-token-not-a-real-secret",
                    ["Twilio:FromNumber"] = "+10000000000",
                    ["Twilio:MessagingServiceSid"] = "MGtestmessagingservicenotreal00000"
                });
            });
        });
    }
}
