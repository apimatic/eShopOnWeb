using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published by <see cref="Services.OrderService"/> after an order is created. Consumed by
/// <see cref="Handlers.RecordOrderUsageHandler"/> to demo "one order placed -> one billable api-call unit" (UC2, §8).
/// </summary>
public sealed record OrderPlaced(string BuyerId, int OrderId) : INotification;
