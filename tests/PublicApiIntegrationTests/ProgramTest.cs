using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Http;
using Microsoft.AspNetCore.Hosting;

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
            builder.UseEnvironment("Testing"));

    }
}
