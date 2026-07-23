using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after an eShopOnWeb order is created. Drives the automatic
/// "one order placed = one billable unit" usage hook (plan.md §8, UC2 trigger).
/// </summary>
/// <remarks>
/// Publication and handling are strictly best-effort: a billing failure must never roll back or block
/// the order lifecycle.
/// </remarks>
public sealed record OrderPlaced(int OrderId, string BuyerId) : INotification;
