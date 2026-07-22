using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process when a customer completes checkout. The subscription feature listens for
/// it so that one order placed bills one metered unit (UC2, decided in plan section 8).
/// </summary>
public record OrderPlaced(string BuyerId, int OrderId) : INotification;
