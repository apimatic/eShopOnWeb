using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Reports at startup whether Maxio billing is usable, so a missing secret shows up in the log
/// on boot instead of only when the first shopper tries to subscribe.
/// </summary>
/// <remarks>
/// It reports rather than throws: the rest of eShopOnWeb (catalog, basket, checkout) does not
/// depend on billing and must still start. The subscription endpoints themselves fail loudly.
/// </remarks>
internal class MaxioConfigurationReporter : IHostedService
{
    private readonly IOptions<MaxioSettings> _options;
    private readonly ILogger<MaxioConfigurationReporter> _logger;

    public MaxioConfigurationReporter(IOptions<MaxioSettings> options,
        ILogger<MaxioConfigurationReporter> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = MaxioOptionsAccessor.Resolve(_options);
            _logger.LogInformation(
                "Maxio billing configured: base address {BaseAddress}, product family '{ProductFamilyHandle}'.",
                settings.ResolveBaseAddress(), settings.ProductFamilyHandle);
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning(
                "Subscription endpoints are unavailable. {Message} See src/PublicApi/README.md for the required Maxio settings.",
                ex.Message);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
