using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The previewed plan-change amount no longer matches what would actually be charged; a fresh preview is required.</summary>
public class PlanChangePreviewStaleException : Exception
{
    public PlanChangePreviewStaleException(string message) : base(message)
    {
    }
}
