using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Shopper-scoped management of the mobile numbers a shopper can be reached on.</summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validate <paramref name="rawNumber"/> with the provider and, if usable, store its canonical form
    /// for <paramref name="buyerId"/>. Rejects an unusable number here rather than at send time.
    /// </summary>
    Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken ct = default);

    /// <summary>The caller's own registered numbers.</summary>
    Task<IReadOnlyList<ContactNumberView>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>
    /// Remove one of the caller's numbers. Returns false if it does not exist or belongs to someone else.
    /// After removal the number no longer appears and nothing is sent to it again.
    /// </summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct = default);
}
