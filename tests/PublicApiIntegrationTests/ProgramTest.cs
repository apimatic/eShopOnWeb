using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net.Http;
using System.Text.Json;

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

    public static IServiceProvider Services => _application.Services;

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
        var apiKey = Environment.GetEnvironmentVariable("MAXIO_API_KEY");
        var subdomain = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");
        var productFamily = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY");

        _application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    if (!string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(subdomain))
                    {
                        services.PostConfigure<MaxioOptions>(options =>
                        {
                            options.ApiKey = apiKey;
                            options.Subdomain = subdomain;
                            if (!string.IsNullOrWhiteSpace(productFamily))
                            {
                                options.ProductFamilyHandle = productFamily;
                            }
                        });
                    }
                });
            });
    }
}
