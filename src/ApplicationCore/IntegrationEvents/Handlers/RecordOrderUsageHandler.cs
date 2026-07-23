using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Bills one metered unit for every order placed (UC2's automatic trigger).
/// <para>
/// Every failure is swallowed deliberately. Usage billing is strictly additive to eShopOnWeb's
/// existing checkout, so a shopper whose order succeeded must never see it fail because the
/// billing provider was unreachable, or because they have no subscription at all — which is the
/// ordinary case for most shoppers.
/// </para>
/// </summary>
public class RecordOrderUsageHandler : INotificationHandler<OrderPlaced>
{
    private const decimal UnitsPerOrder = 1m;

    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<RecordOrderUsageHandler> _logger;

    public RecordOrderUsageHandler(ISubscriptionService subscriptionService,
        IAppLogger<RecordOrderUsageHandler> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public async Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        try
        {
            var usage = await _subscriptionService.RecordUsageAsync(notification.BuyerId,
                UnitsPerOrder,
                $"Order {notification.Order.Id} placed",
                cancellationToken);

            _logger.LogInformation(
                $"Recorded {UnitsPerOrder} usage unit for order {notification.Order.Id}; " +
                $"period-to-date total is now {usage.PeriodToDateTotal?.ToString() ?? "unavailable"}.");
        }
        catch (NoActiveSubscriptionException)
        {
            // The shopper simply has no subscription. Expected, and not worth a warning.
        }
        catch (InvalidSubscriptionTransitionException)
        {
            // Their subscription is not live (paused, cancelled). Nothing to bill.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                $"Order {notification.Order.Id} was placed successfully but usage could not be " +
                $"recorded against the buyer's subscription: {exception.Message}");
        }
    }
}
