using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Signals that Maxio rejected a POST as a duplicate (HTTP 409) because it reused a
/// uniqueness_token seen within the last 60 minutes - see Maxio's Duplicate Prevention docs.
/// Callers should treat this as "the operation already happened" and re-fetch the result.
/// </summary>
internal class MaxioDuplicateRequestException : Exception
{
}
