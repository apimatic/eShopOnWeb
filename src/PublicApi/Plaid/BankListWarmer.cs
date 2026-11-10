using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>
/// Fills the bank picker's list in the background, so no shopper waits for the paged walk
/// over Plaid's rate-limited institutions endpoint when checkout opens.
/// </summary>
public class BankListWarmer : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptionsMonitor<PlaidOptions> _options;
    private readonly ILogger<BankListWarmer> _logger;

    public BankListWarmer(IServiceScopeFactory scopes, IOptionsMonitor<PlaidOptions> options, ILogger<BankListWarmer> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromMinutes(5);
            if (_options.CurrentValue.HasCredentials && _options.CurrentValue.ResolveBaseUrl() is not null)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<BankPickerService>().RefreshAsync(stoppingToken);
                    delay = BankPickerService.RefreshEvery;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not warm the bank list; will retry.");
                }
            }
            try { await Task.Delay(delay, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
