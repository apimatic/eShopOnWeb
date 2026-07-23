using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the proration a customer was shown no longer matches what the provider would
/// charge at commit time (UC3). The plan change is refused rather than applied at a different
/// amount; the customer must be shown a fresh preview.
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    /// <summary>The amount, in cents, the customer was shown and confirmed.</summary>
    public long ExpectedPaymentDueInCents { get; }

    /// <summary>The amount, in cents, the provider would charge right now.</summary>
    public long ActualPaymentDueInCents { get; }

    public StalePlanChangePreviewException(long expectedPaymentDueInCents, long actualPaymentDueInCents)
        : base(BuildMessage(expectedPaymentDueInCents, actualPaymentDueInCents))
    {
        ExpectedPaymentDueInCents = expectedPaymentDueInCents;
        ActualPaymentDueInCents = actualPaymentDueInCents;
    }

    // Formatted as "$0.00" rather than with the "C" specifier: under the invariant culture "C"
    // renders the generic currency placeholder, which reads as mojibake to a customer.
    private static string BuildMessage(long expected, long actual) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "The plan change was previewed at ${0:N2} but now costs ${1:N2}. Review the updated preview before confirming.",
            expected / 100m,
            actual / 100m);
}
