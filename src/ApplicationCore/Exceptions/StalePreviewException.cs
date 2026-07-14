using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a plan-change commit's freshly re-run preview no longer matches the preview the
/// customer was shown (price or proration basis moved between preview and confirm). The commit is
/// rejected; the caller must request a fresh preview before trying again.
/// </summary>
public class StalePreviewException : Exception
{
    public StalePreviewException(string message) : base(message)
    {
    }
}
