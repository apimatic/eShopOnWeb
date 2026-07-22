using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after an eShopOnWeb order has been created. This is the hook that turns
/// "one order placed" into "one billable unit" of metered usage (plan.md §8, UC2).
/// <para>
/// Publication is best-effort and deliberately isolated: a handler that fails — including the usage
/// handler when the billing provider is unreachable — is logged and swallowed, so the existing
/// order lifecycle is never rolled back or blocked by a billing problem.
/// </para>
/// </summary>
/// <param name="OrderId">The identifier of the order that was created.</param>
/// <param name="BuyerId">The eShopOnWeb buyer reference (email/username) that placed the order.</param>
public record OrderPlaced(int OrderId, string BuyerId) : INotification;
