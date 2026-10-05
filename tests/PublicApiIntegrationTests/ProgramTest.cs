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
        // Placeholders (not secrets) so the Maxio fail-fast validation lets the host start
        // in environments without Maxio configuration; Maxio-dependent tests are skipped
        // unless the real environment variables are present.
        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Maxio:ApiKey", "placeholder-not-a-secret");
            builder.UseSetting("Maxio:Subdomain", "placeholder-site");
            builder.UseSetting("Maxio:ProductFamilyHandle", "placeholder-family");
        });
    }
}
