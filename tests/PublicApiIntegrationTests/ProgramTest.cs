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
        // The Mindbody sandbox credentials come from the environment (or user-secrets when the host loads them);
        // nothing is stored in the repository.
        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>();
                void FromEnv(string key, string variable)
                {
                    var value = System.Environment.GetEnvironmentVariable(variable);
                    if (!string.IsNullOrEmpty(value)) settings[key] = value;
                }

                FromEnv("Mindbody:ApiKey", "MINDBODY_API_KEY");
                FromEnv("Mindbody:SiteId", "MINDBODY_SITE_ID");
                FromEnv("Mindbody:Username", "MINDBODY_USERNAME");
                FromEnv("Mindbody:Password", "MINDBODY_PASSWORD");
                config.AddInMemoryCollection(settings);
            }));
    }
}
