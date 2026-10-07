using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PublicApiIntegrationTests.DigitalFiles;
using System.Net.Http;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    private static WebApplicationFactory<Program> _application = new TestApiFactory();

    /// <summary>
    /// Stands in for the merchant's Box account so the tests never touch the network.
    /// </summary>
    public static FakeDigitalFileStorage Storage { get; } = new();

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
        _application = new TestApiFactory();
    }

    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Not a credential: the Box client is never called, but the host refuses to start without a token.
            builder.UseSetting("Box:AccessToken", "test-placeholder");
            builder.UseSetting("Box:StallTimeout", "00:00:00.500");
            builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IDigitalFileStorage>(Storage)));
        }
    }
}
