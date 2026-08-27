using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class ValidatedPhoneNumber
{
    public bool Valid { get; set; }

    /// <summary>Canonical E.164 form assigned by the provider.</summary>
    public string? PhoneNumber { get; set; }

    public string? NationalFormat { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
}

public interface IPhoneNumberValidator
{
    /// <summary>
    /// Asks the provider whether the raw input is a usable destination and, if so,
    /// returns the provider's canonical form of the number.
    /// </summary>
    Task<ValidatedPhoneNumber> ValidateAsync(string rawNumber, string? countryCode = null, CancellationToken cancellationToken = default);
}
