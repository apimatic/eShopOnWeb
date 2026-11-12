using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Net.Http;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    private static WebApplicationFactory<Program> _application = CreateApplication();

    public static IServiceProvider Services => _application.Services;

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
        _application = CreateApplication();
    }

    /// <summary>
    /// The Maxio sandbox settings come from the environment (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN,
    /// MAXIO_ENVIRONMENT) and reach the app through its Maxio: configuration section.
    /// </summary>
    private static WebApplicationFactory<Program> CreateApplication()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>();
                void Map(string variable, string key)
                {
                    var value = Environment.GetEnvironmentVariable(variable);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        settings[key] = value;
                    }
                }
                Map("MAXIO_API_KEY", "Maxio:ApiKey");
                Map("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
                Map("MAXIO_ENVIRONMENT", "Maxio:Environment");
                config.AddInMemoryCollection(settings);
            });
        });
    }
}
