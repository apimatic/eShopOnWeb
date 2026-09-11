using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi;

public class SubscriptionService
{
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly MaxioAdvancedBillingClient _client;

    public SubscriptionService(IConfiguration config, ILogger<SubscriptionService> logger)
    {
        _logger = logger;
        _settings = new MaxioSettings();
        config.GetSection("Maxio").Bind(_settings);

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default(),
        };

        // Basic auth: API key as username, literal "x" as password
        options.BasicAuth = new BasicAuthCredentials
        {
            Username = _settings.ApiKey,
            Password = "x"
        };

        // Server override if configured
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = _settings.BaseUrl;
        }
        else if (!string.IsNullOrWhiteSpace(_settings.Subdomain))
        {
            // SDK derives URL from subdomain + environment when not overridden
        }

        var httpClient = new HttpClient();
        _client = new MaxioAdvancedBillingClient(httpClient, options);
    }

    public MaxioAdvancedBillingClient Client => _client;
}
