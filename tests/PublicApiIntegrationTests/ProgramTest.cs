using Microsoft.AspNetCore.Mvc.Testing;
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

    /// <summary>The shared test host, e.g. to reach the in-memory identity store.</summary>
    public static WebApplicationFactory<Program> Application => _application;

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
        _application = new WebApplicationFactory<Program>();

    }
}
