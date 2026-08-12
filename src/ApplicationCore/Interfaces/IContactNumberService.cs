using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Manages the mobile numbers a shopper has on file. Every operation is scoped to one shopper.</summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validate a number with the provider and, if usable, store its canonical E.164 form for the shopper.
    /// A number the provider rejects is not stored.
    /// </summary>
    Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>The caller's own registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Remove one of the caller's numbers. Returns false if it is not found among the caller's numbers.</summary>
    Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
