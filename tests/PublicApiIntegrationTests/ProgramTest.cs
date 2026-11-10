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

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
        // Map the sandbox credentials from the environment onto the Mindbody: section.
        foreach (var key in new[] { "ApiKey", "SiteId", "Username", "Password" })
        {
            var value = System.Environment.GetEnvironmentVariable("MINDBODY_" + key.ToUpperInvariant());
            if (value != null) System.Environment.SetEnvironmentVariable("Mindbody__" + key, value);
        }
        _application = new WebApplicationFactory<Program>();

    }
}
