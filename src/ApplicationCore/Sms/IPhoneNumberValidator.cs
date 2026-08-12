using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// Asks the provider whether a number is a usable destination, and for its canonical form, so a
/// number the provider does not consider reachable is rejected at registration rather than at the
/// moment a message fails to go out.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
