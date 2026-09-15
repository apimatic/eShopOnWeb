using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers a shopper has put on file. Every operation is scoped to a single
/// owner (the signed-in shopper): one shopper can never see, use, or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for <paramref name="ownerId"/> after validating it with the provider.
    /// The provider's canonical E.164 form is what gets stored. A number the provider does not
    /// consider a usable destination is rejected here (<see cref="ActionOutcome.BadRequest"/>).
    /// </summary>
    Task<RegisterContactNumberResult> RegisterAsync(string ownerId, string rawNumber);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumberView>> ListAsync(string ownerId);

    /// <summary>
    /// Removes one of the caller's numbers. Returns false if it does not exist or is not owned by
    /// the caller. Afterwards nothing is ever sent to it again.
    /// </summary>
    Task<bool> DeleteAsync(string ownerId, int contactNumberId);
}
