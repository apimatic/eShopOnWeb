using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Configuration;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    private static WebApplicationFactory<Program> _application = CreateFactory();

    public static HttpClient NewClient
    {
        get
        {
            return _application.CreateClient();
        }
    }

    /// <summary>
    /// The PublicApi host validates its Maxio:* settings at startup. These are NON-SECRET placeholders so the
    /// host boots; no test here calls the billing provider, and the placeholders override any local user-secrets.
    /// </summary>
    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Maxio:ApiKey"] = "placeholder-not-a-real-key",
                ["Maxio:Subdomain"] = "placeholder-site",
                ["Maxio:ProductFamilyHandle"] = "placeholder-family",
                ["Maxio:BaseUrl"] = "https://maxio.invalid",
            })));

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
        _application = CreateFactory();

    }
}
