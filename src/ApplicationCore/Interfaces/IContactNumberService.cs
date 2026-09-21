using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Manages the mobile numbers a shopper has on file. Every operation is scoped to one shopper.</summary>
public interface IContactNumberService
{
    /// <summary>Register a number for a shopper. Rejects a number the provider does not consider a usable
    /// destination (throws <see cref="Exceptions.InvalidPhoneNumberException"/>); stores the provider's
    /// canonical E.164 form.</summary>
    Task<ContactNumber> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Remove one of the shopper's own numbers. Returns false if it does not exist or belongs to someone else.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken);
}
