using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public interface IPhoneNumberLookup
{
    Task<PhoneLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken);
}

public sealed record PhoneLookupResult(
    bool ProviderCallSucceeded,
    bool IsUsable,
    string? CanonicalNumber,
    string? FailureMessage,
    bool IsCallerFault)
{
    public static PhoneLookupResult Usable(string canonicalNumber) =>
        new(true, true, canonicalNumber, null, false);

    public static PhoneLookupResult NotUsable(string message) =>
        new(true, false, null, message, true);

    public static PhoneLookupResult ProviderFault(string message) =>
        new(false, false, null, message, false);
}
