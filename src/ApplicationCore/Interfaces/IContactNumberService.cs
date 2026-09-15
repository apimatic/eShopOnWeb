using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    /// <summary>
    /// Register a mobile number for a shopper. The number is validated with the provider and stored
    /// in the provider's canonical form. A number the provider does not consider a usable destination
    /// is rejected here, before any message is ever attempted.
    /// </summary>
    Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default);

    /// <summary>The caller's own registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove one of the caller's numbers. Returns false if it does not exist or does not belong to
    /// the caller. Any message still queued for a future send to that number is called off so nothing
    /// can be sent to it again.
    /// </summary>
    Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of registering a contact number.</summary>
public record RegisterContactNumberResult(bool Success, ContactNumber? ContactNumber, string? Error)
{
    public static RegisterContactNumberResult Ok(ContactNumber number) => new(true, number, null);
    public static RegisterContactNumberResult Rejected(string error) => new(false, null, error);
}
