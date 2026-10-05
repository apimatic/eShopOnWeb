using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscription;

/// <summary>
/// A subscribable plan (a Maxio product) as exposed to shoppers.
/// </summary>
public class SubscriptionPlanInfo
{
    /// <summary>
    /// Stable Maxio API handle of the product (e.g. "eshop-pro").
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Recurring price per billing interval in the site currency.
    /// </summary>
    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Number of interval units between renewals (e.g. 1).
    /// </summary>
    public int Interval { get; set; }

    /// <summary>
    /// Maxio billing interval unit - "month" or "day".
    /// </summary>
    public string IntervalUnit { get; set; } = string.Empty;

    public bool Taxable { get; set; }
}

/// <summary>
/// Identity of the eShopOnWeb user that a Maxio customer record maps to.
/// </summary>
public class SubscriptionCustomerRequest
{
    public SubscriptionCustomerRequest(string userId, string email, string firstName, string lastName)
    {
        UserId = userId;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>
    /// Unique, stable id of the eShopOnWeb user (used as the Maxio customer reference).
    /// </summary>
    public string UserId { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }
}

/// <summary>
/// Result of making sure a Maxio customer exists for an eShopOnWeb user.
/// </summary>
public class SubscriptionCustomerResult
{
    public SubscriptionCustomerResult(int maxioCustomerId, string reference, bool created)
    {
        MaxioCustomerId = maxioCustomerId;
        Reference = reference;
        Created = created;
    }

    public int MaxioCustomerId { get; }

    /// <summary>
    /// The Maxio customer reference this app maps the user by.
    /// </summary>
    public string Reference { get; }

    /// <summary>
    /// True when the customer was created by this call, false when it already existed.
    /// </summary>
    public bool Created { get; }
}

/// <summary>
/// A request to enroll a user in a plan.
/// </summary>
public class SubscriptionSignupRequest
{
    public SubscriptionSignupRequest(string userId, string email, string firstName, string lastName, string productHandle)
    {
        UserId = userId;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        ProductHandle = productHandle;
    }

    public string UserId { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Maxio product handle to subscribe to (e.g. "eshop-pro").
    /// </summary>
    public string ProductHandle { get; }
}

/// <summary>
/// Full state of a subscription after signup, confirmed back to the caller.
/// </summary>
public class SubscriptionDetails
{
    public int SubscriptionId { get; set; }

    /// <summary>
    /// Maxio subscription state - e.g. "active", "trialing", "past_due", "canceled".
    /// </summary>
    public string State { get; set; } = string.Empty;

    public string ProductHandle { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Recurring price of this subscription, per billing interval.
    /// </summary>
    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Next date the subscription will be assessed / billed (null for ended subscriptions).
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public int MaxioCustomerId { get; set; }

    /// <summary>
    /// True when this call returned an already-existing subscription instead of creating one.
    /// </summary>
    public bool WasExisting { get; set; }
}

/// <summary>
/// A subscription belonging to a user, as shown in their account.
/// </summary>
public class SubscriptionSummary
{
    public int SubscriptionId { get; set; }

    public string State { get; set; } = string.Empty;

    public string ProductHandle { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}