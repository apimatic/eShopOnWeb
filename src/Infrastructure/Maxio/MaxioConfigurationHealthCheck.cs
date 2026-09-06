using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Reports the state of the Maxio configuration once at start-up.
/// </summary>
/// <remarks>
/// Subscriptions are additive, so a missing configuration must not stop the host: the catalog,
/// basket and order endpoints have nothing to do with billing. This makes the problem obvious in
/// the log at boot instead, while the subscription endpoints themselves fail with the same
/// validation messages when they are called.
/// </remarks>
public class MaxioConfigurationHealthCheck : IHostedService
{
    private readonly IOptions<MaxioSettings> _settings;
    private readonly ILogger<MaxioConfigurationHealthCheck> _logger;

    public MaxioConfigurationHealthCheck(IOptions<MaxioSettings> settings, ILogger<MaxioConfigurationHealthCheck> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = _settings.Value;

            _logger.LogInformation(
                "Maxio subscriptions are configured for site '{Subdomain}' at {BaseAddress} using product family '{ProductFamilyHandle}'.",
                settings.Subdomain,
                settings.ResolveBaseAddress(),
                settings.ProductFamilyHandle);
        }
        catch (OptionsValidationException ex)
        {
            _logger.LogError(
                "Maxio subscriptions are not configured, so the subscription endpoints will fail: {Failures} "
                + "Set Maxio:ApiKey, Maxio:Subdomain and Maxio:ProductFamilyHandle (user-secrets, or the "
                + "MAXIO_API_KEY / MAXIO_SITE_SUBDOMAIN / MAXIO_DEFAULT_PRODUCT_FAMILY environment variables).",
                string.Join(" ", ex.Failures));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
