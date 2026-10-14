using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Http;
using PublicApiIntegrationTests.SubscriptionEndpoints;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    // Fake Maxio settings + in-process Maxio stub: the host validates billing settings on start.
    private static WebApplicationFactory<Program> _application = new PublicApiTestFactory();

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
        _application = new PublicApiTestFactory();

    }
}
