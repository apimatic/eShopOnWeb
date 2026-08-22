using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string phoneNumber, string? countryCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<ContactNumberDeleteResult> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}

public sealed class ContactNumberRegistrationResult
{
    public bool Succeeded { get; init; }
    public int StatusCode { get; init; }
    public string? Error { get; init; }
    public ShopperContactNumber? ContactNumber { get; init; }
}

public sealed class ContactNumberDeleteResult
{
    public bool Succeeded { get; init; }
    public int StatusCode { get; init; }
    public string? Error { get; init; }
}
