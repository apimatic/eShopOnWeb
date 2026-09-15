using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's registered mobile numbers. Every operation is scoped to a single owner; one
/// shopper can never see, use or delete another's numbers.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for the shopper after confirming with the provider that it is a usable
    /// destination, storing the provider's canonical form. Returns Invalid when the number is not usable.
    /// </summary>
    Task<Result<ContactNumber>> RegisterAsync(string ownerId, string rawPhoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Lists the owner's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the owner's numbers. Returns NotFound when it isn't the owner's / doesn't exist.</summary>
    Task<Result> RemoveAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);
}
