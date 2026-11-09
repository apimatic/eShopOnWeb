using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class PaymentsSetup
{
    public static IServiceCollection AddStaxPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StaxSettings>(configuration.GetSection(StaxSettings.SECTION));
        services.Configure<PaymentsSettings>(configuration.GetSection(PaymentsSettings.SECTION));

        var inMemory = bool.TryParse(configuration["UseOnlyInMemoryDatabase"], out var flag) && flag;
        if (inMemory) services.AddDbContext<PaymentsDbContext>(o => o.UseInMemoryDatabase("Payments"));
        else services.AddDbContext<PaymentsDbContext>(o => o.UseSqlServer(configuration.GetConnectionString("CatalogConnection")));

        services.AddHttpClient();
        services.AddSingleton<StaxGateway>();
        services.AddScoped<ShopperService>();
        services.AddScoped<PaymentMethodService>();
        services.AddScoped<OrderPaymentService>();
        services.AddScoped<InvoiceScheduleService>();
        services.AddScoped<SellerService>();
        services.AddScoped<AccountService>();
        services.AddHostedService<PaymentSyncWorker>();
        return services;
    }

    /// <summary>Creates the payment tables next to the catalog tables when they are missing.</summary>
    public static async Task EnsurePaymentsDatabase(this PaymentsDbContext db)
    {
        if (!db.Database.IsRelational())
        {
            await db.Database.EnsureCreatedAsync();
            return;
        }
        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync()) await creator.CreateAsync();
        try { await creator.CreateTablesAsync(); }
        catch (Exception ex) when (ex.Message.Contains("already an object named", StringComparison.OrdinalIgnoreCase)
                                   || ex.InnerException?.Message.Contains("already an object named", StringComparison.OrdinalIgnoreCase) == true)
        {
            // tables from an earlier start are already there
        }
    }
}

/// <summary>
/// Bank payments settle days after they are made and bank refunds can only be made once the payment has settled. Stax cannot
/// call the shop back from a sandbox, so this worker periodically re-checks them and completes whatever has become possible.
/// </summary>
public class PaymentSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PaymentsSettings _settings;
    private readonly ILogger<PaymentSyncWorker> _logger;

    public PaymentSyncWorker(IServiceScopeFactory scopes, IOptions<PaymentsSettings> settings, ILogger<PaymentSyncWorker> logger)
    {
        _scopes = scopes;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _settings.SyncIntervalSeconds));
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnce(stoppingToken); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { _logger.LogError(ex, "Payment reconciliation failed."); }
            try { await Task.Delay(interval, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    public async Task RunOnce(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<OrderPaymentService>();
        foreach (var id in await service.PendingOrderIds(ct))
        {
            try { await service.Reconcile(id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Reconciling order {OrderId} failed.", id); }
        }
    }
}
