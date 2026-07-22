using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announced in-process once an eShopOnWeb order has been persisted. This is the hook UC2 uses to turn
/// "one order placed" into "one billable unit" — see
/// <see cref="Handlers.RecordUsageOnOrderPlacedHandler"/>. Publication is best-effort and deliberately
/// happens after the order is saved, so no billing concern can block or roll back the order lifecycle.
/// </summary>
public record OrderPlaced(int OrderId, string BuyerId) : INotification;
