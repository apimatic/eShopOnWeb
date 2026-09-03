using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPhoneNumberLookupService
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken);
}

public sealed class PhoneNumberLookupResult
{
    public bool IsUsable { get; init; }
    public string? CanonicalNumber { get; init; }
}
