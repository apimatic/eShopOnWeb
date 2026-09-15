using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ContactNumberRecord(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);

public interface IContactNumberService
{
    Task<ContactNumberRecord> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContactNumberRecord>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
