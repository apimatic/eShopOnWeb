using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A shopper's mobile contact numbers on file. Every method is scoped to the calling shopper — one
/// shopper never sees, uses or deletes another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validates and registers a mobile number for the shopper. Rejects a number the provider does not
    /// consider a usable destination (returns null), and stores the provider's canonical E.164 form.
    /// </summary>
    Task<ContactNumberDto?> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContactNumberDto>> ListAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Removes one of the shopper's own numbers. Returns false if it is not theirs / not found.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken);
}
