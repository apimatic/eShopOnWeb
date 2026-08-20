using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record LookupPhoneNumberResult(bool Valid, string? PhoneNumber, string? NationalFormat, IReadOnlyList<string> ValidationErrors);

public interface IPhoneNumberLookupService
{
    Task<LookupPhoneNumberResult> LookupAsync(string rawPhoneNumber, CancellationToken cancellationToken = default);
}
