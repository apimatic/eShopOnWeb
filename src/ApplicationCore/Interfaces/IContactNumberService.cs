using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Shopper-scoped management of the mobile numbers a shopper has on file.</summary>
public interface IContactNumberService
{
    /// <summary>
    /// Register a number for the shopper. The provider validates it and returns its canonical form,
    /// which is what gets stored. Throws <see cref="SmsGatewayException"/> if the provider is unreachable.
    /// </summary>
    Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken ct);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumberView>> ListAsync(string buyerId, CancellationToken ct);

    /// <summary>Remove one of the caller's numbers. Returns false if it is not the caller's / does not exist.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct);
}

public sealed record RegisterContactNumberResult(bool Registered, int? ContactNumberId, string? Reason);

public sealed record ContactNumberView(int ContactNumberId, string E164Number);
