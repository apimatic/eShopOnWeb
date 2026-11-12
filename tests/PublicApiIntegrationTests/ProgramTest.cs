using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    private static WebApplicationFactory<Program> _application = Create();

    /// <summary>
    /// The Maxio settings come from user-secrets (Development) or, on a machine without them, from the
    /// sandbox environment variables - never from a file in the repository.
    /// </summary>
    private static WebApplicationFactory<Program> Create() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
        {
            var fromEnv = new Dictionary<string, string?>();
            void Map(string key, string variable)
            {
                var value = System.Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrWhiteSpace(value)) fromEnv[key] = value;
            }
            Map("Maxio:ApiKey", "MAXIO_API_KEY");
            Map("Maxio:Subdomain", "MAXIO_SITE_SUBDOMAIN");
            Map("Maxio:Environment", "MAXIO_ENVIRONMENT");
            if (fromEnv.Count > 0) config.AddInMemoryCollection(fromEnv);
        }));

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
        _application = Create();

    }
}
