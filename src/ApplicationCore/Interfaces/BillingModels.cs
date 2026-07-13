using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

// Provider-agnostic shapes returned by IBillingClient. These describe what eShopOnWeb
// needs from a recurring-billing provider; nothing here is Maxio-specific.

public class BillingPlan
{
    public BillingPlan(string handle, string name, int priceInCents, int interval, string intervalUnit)
    {
        Handle = handle;
        Name = name;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
    }

    public string Handle { get; }
    public string Name { get; }
    public int PriceInCents { get; }
    public int Interval { get; }
    public string IntervalUnit { get; }
}

public class BillingCustomer
{
    public BillingCustomer(int id, string reference)
    {
        Id = id;
        Reference = reference;
    }

    public int Id { get; }
    public string Reference { get; }
}

public class BillingSubscription
{
    public BillingSubscription(
        int id,
        string state,
        int customerId,
        string? customerReference,
        string productHandle,
        string productName,
        int priceInCents,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt,
        bool cancelAtEndOfPeriod)
    {
        Id = id;
        State = state;
        CustomerId = customerId;
        CustomerReference = customerReference;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
    }

    public int Id { get; }
    public string State { get; }
    public int CustomerId { get; }
    public string? CustomerReference { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public int PriceInCents { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public DateTimeOffset? NextAssessmentAt { get; }
    public bool CancelAtEndOfPeriod { get; }
}

public class BillingComponent
{
    public BillingComponent(int id, string handle, string name, string kind)
    {
        Id = id;
        Handle = handle;
        Name = name;
        Kind = kind;
    }

    public int Id { get; }
    public string Handle { get; }
    public string Name { get; }
    public string Kind { get; }

    public bool IsMetered => string.Equals(Kind, "metered_component", StringComparison.OrdinalIgnoreCase);
}

public class BillingUsageRecord
{
    public BillingUsageRecord(long id, int quantity, string? memo)
    {
        Id = id;
        Quantity = quantity;
        Memo = memo;
    }

    public long Id { get; }
    public int Quantity { get; }
    public string? Memo { get; }
}

public class BillingPlanChangePreview
{
    public BillingPlanChangePreview(int proratedAdjustmentInCents, int chargeInCents, int paymentDueInCents, int creditAppliedInCents)
    {
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        PaymentDueInCents = paymentDueInCents;
        CreditAppliedInCents = creditAppliedInCents;
    }

    public int ProratedAdjustmentInCents { get; }
    public int ChargeInCents { get; }
    public int PaymentDueInCents { get; }
    public int CreditAppliedInCents { get; }
}
