using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class PhoneNumberLookupResult
{
    public PhoneNumberLookupResult(bool isValid, string? canonicalPhoneNumber, IReadOnlyList<string>? validationErrors = null)
    {
        IsValid = isValid;
        CanonicalPhoneNumber = canonicalPhoneNumber;
        ValidationErrors = validationErrors ?? Array.Empty<string>();
    }

    public bool IsValid { get; }
    public string? CanonicalPhoneNumber { get; }
    public IReadOnlyList<string> ValidationErrors { get; }
}

public interface IPhoneNumberLookupService
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
