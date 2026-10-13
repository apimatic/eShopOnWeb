using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using WalmartApIs;
using WalmartApIs.Core.Authentication.OAuth2.ClientCredentials;
using WalmartApIs.Core.Configuration;
using WalmartApIs.Servers;

namespace PublicApiIntegrationTests.WalmartStockEndpoints;

/// <summary>
/// Boots the real PublicApi app but points the Walmart SDK client at an in-memory <see cref="FakeWalmartHandler"/>
/// (no live Walmart, retries disabled for fast failure tests) and supplies dummy credentials so startup
/// validation passes without depending on user-secrets.
/// </summary>
public sealed class WalmartTestWebApplicationFactory : WebApplicationFactory<Program>
{
    public FakeWalmartHandler Handler { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Walmart:ClientId"] = "test-client-id",
                ["Walmart:ClientSecret"] = "test-client-secret",
                ["UseOnlyInMemoryDatabase"] = "true",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<WalmartApIsClient>();
            services.AddSingleton(sp =>
            {
                var options = new WalmartApIsClientOptions
                {
                    Environment = ServerEnvironment.Production,
                    WalletAuth = new OAuth2ClientCredentials { ClientId = "test-client-id", ClientSecret = "test-client-secret" },
                    // Disable retries so a simulated transport failure surfaces immediately (no backoff wait).
                    Retry = RetryOptions.Disabled() with { Timeout = TimeSpan.FromSeconds(10) },
                    Logging = new LoggingOptions { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() },
                };
                return new WalmartApIsClient(new HttpClient(Handler), options);
            });
        });
    }

    public void ResetWalmart()
    {
        Handler.OnHand.Clear();
        Handler.Calls.Clear();
        Handler.FailUpdateForSku = null;
        Handler.FailAllUpdatesWithTransportError = false;
    }
}
