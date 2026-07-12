using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The proration basis changed between preview and commit (UC3). Carries a fresh preview so the
/// caller can show the customer the updated amount instead of silently applying a different one.
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public PlanChangePreview FreshPreview { get; }

    public StalePlanChangePreviewException(PlanChangePreview freshPreview)
        : base("The previewed plan-change amount is stale; a fresh preview is required before committing.")
    {
        FreshPreview = freshPreview;
    }
}
