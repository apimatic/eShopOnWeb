using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using System.Collections.Generic;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    private static WebApplicationFactory<Program> _application = CreateFactory();

    public static FakeMessagingProvider MessagingProvider =>
        _application.Services.GetRequiredService<FakeMessagingProvider>();

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
        _application = CreateFactory();

    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Twilio:AccountSid"] = "integration-test-account",
                    ["Twilio:AuthToken"] = "integration-test-token",
                    ["Twilio:FromNumber"] = "integration-test-sender",
                    ["Twilio:MessagingServiceSid"] = "integration-test-service"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMessagingProvider>();
                services.AddSingleton<FakeMessagingProvider>();
                services.AddSingleton<IMessagingProvider>(provider => provider.GetRequiredService<FakeMessagingProvider>());
            });
        });
}
