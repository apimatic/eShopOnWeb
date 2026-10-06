using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PublicApiIntegrationTests.Square;
using System.Net.Http;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    // Test Square settings and a fake Square transport: these tests never depend on real credentials.
    private static WebApplicationFactory<Program> _application = new SquareApiFactory(isolatedCatalog: false);

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
        _application = new SquareApiFactory(isolatedCatalog: false);

    }
}
