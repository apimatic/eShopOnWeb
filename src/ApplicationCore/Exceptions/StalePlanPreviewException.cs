using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class StalePlanPreviewException : Exception
{
    public StalePlanPreviewException()
        : base("The proration preview is no longer current. Request a fresh preview before confirming the plan change.")
    {
    }
}
