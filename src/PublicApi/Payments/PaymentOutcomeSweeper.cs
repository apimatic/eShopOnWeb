using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Settles PayPal writes whose outcome is unknown (PayPal did not answer in time) or whose request was lost
/// mid-flight, by re-reading PayPal or re-sending under the stored PayPal-Request-Id. Each item runs in its own
/// scope, so it gets its own PayPal time budget.
/// </summary>
public class PaymentOutcomeSweeper : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PaymentOutcomeSweeper> _logger;
    private readonly TimeSpan _interval;

    public PaymentOutcomeSweeper(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<PaymentOutcomeSweeper> logger)
    {
        _scopes = scopes;
        _logger = logger;
        _interval = configuration.GetValue<TimeSpan?>("PayPal:SweepInterval") ?? TimeSpan.FromMinutes(1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_interval <= TimeSpan.Zero)
        {
            _logger.LogInformation("Payment outcome sweeper disabled (PayPal:SweepInterval <= 0).");
            return;
        }

        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Payment outcome sweep failed.");
            }
        }
    }

    public async Task SweepOnceAsync(CancellationToken ct)
    {
        int[] paymentIds;
        int[] methodIds;
        using (var scope = _scopes.CreateScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<PaymentSettings>();
            var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
            var staleBefore = clock.GetUtcNow() - settings.StaleAfter;
            paymentIds = (await scope.ServiceProvider.GetRequiredService<IRepository<OrderPayment>>()
                .ListAsync(new PaymentsNeedingSettlementSpec(staleBefore), ct)).Select(p => p.Id).ToArray();
            methodIds = (await scope.ServiceProvider.GetRequiredService<IRepository<SavedPaymentMethod>>()
                .ListAsync(new SavedPaymentMethodsNeedingSettlementSpec(staleBefore), ct)).Select(m => m.Id).ToArray();
        }

        foreach (var id in paymentIds)
        {
            using var scope = _scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<PaymentService>().SettleAsync(id, ct);
        }

        foreach (var id in methodIds)
        {
            using var scope = _scopes.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<PaymentMethodService>().SettleAsync(id, ct);
            }
            catch (PaymentGatewayException ex)
            {
                _logger.LogWarning("Settling saved card {PaymentMethodId} did not finish: {Failure}", id, ex.Failure);
            }
        }

        if (paymentIds.Length + methodIds.Length > 0)
            _logger.LogInformation("Payment outcome sweep visited {Payments} payment(s) and {Methods} saved card(s).", paymentIds.Length, methodIds.Length);
    }
}
