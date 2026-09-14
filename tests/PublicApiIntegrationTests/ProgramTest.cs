using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    private static SubscriptionTestApplication _application = new();

    public static FakeMaxioSubscriptionGateway Maxio => _application.Maxio;

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
        _application = new SubscriptionTestApplication();

    }
}

public sealed class SubscriptionTestApplication : WebApplicationFactory<Program>
{
    public FakeMaxioSubscriptionGateway Maxio { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMaxioSubscriptionGateway>();
            services.AddSingleton<IMaxioSubscriptionGateway>(Maxio);
        });
    }
}
