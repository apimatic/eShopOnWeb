using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system rejected the request as invalid. Retrying the same request unchanged will
/// fail the same way; the caller has to change something.
/// </summary>
public class BillingValidationException : BillingException
{
    public BillingValidationException(IEnumerable<string> errors)
        : base(Describe(errors, out var materialized))
    {
        Errors = materialized;
    }

    /// <summary>The individual rejection reasons reported by the billing system.</summary>
    public IReadOnlyCollection<string> Errors { get; }

    private static string Describe(IEnumerable<string> errors, out IReadOnlyCollection<string> materialized)
    {
        materialized = errors?.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray() ?? System.Array.Empty<string>();

        return materialized.Count == 0
            ? "The billing system rejected the request."
            : "The billing system rejected the request: " + string.Join("; ", materialized);
    }
}
