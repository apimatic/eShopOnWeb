using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net.Http;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    private static WebApplicationFactory<Program> _application = null!;

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
        var testConfigurationSuffix = Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable("Maxio__ApiKey", $"test-{testConfigurationSuffix}");
        Environment.SetEnvironmentVariable("Maxio__Subdomain", $"test-{testConfigurationSuffix}");
        Environment.SetEnvironmentVariable("Maxio__ProductFamilyHandle", $"test-{testConfigurationSuffix}");
        _application = new WebApplicationFactory<Program>();
    }
}
