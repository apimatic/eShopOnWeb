using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when a plan-change commit is attempted with a preview token that has expired or was never issued.</summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException() : base("This plan change preview is no longer valid. Request a fresh preview before committing.")
    {
    }
}
