using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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

    public static IConfiguration Configuration => _application.Services.GetRequiredService<IConfiguration>();

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
        _application = new WebApplicationFactory<Program>();

    }
}
