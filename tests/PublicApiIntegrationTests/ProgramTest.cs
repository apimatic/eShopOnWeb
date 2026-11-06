using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Http;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    private static WebApplicationFactory<Program> _application = CreateFactory();

    // The Maxio sandbox credentials are read from .NET user-secrets (Maxio:ApiKey, Maxio:Subdomain, ...);
    // nothing sensitive lives in the repository.
    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddUserSecrets<Program>(optional: true)));

    public static HttpClient NewClient
    {
        get
        {
            return _application.CreateClient();
        }
    }

    public static IConfiguration Configuration => (IConfiguration)_application.Services.GetService(typeof(IConfiguration))!;

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
        _application = CreateFactory();
    }
}
