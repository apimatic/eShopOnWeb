namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Request body for <c>POST /customers.json</c> (createCustomer).
/// Mirrors <c>Create-Customer-Request.yaml</c> / <c>Create-Customer.yaml</c> from the Maxio spec.
/// </summary>
public sealed class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomerRequest(MaxioCreateCustomer customer)
    {
        Customer = customer;
    }

    public MaxioCreateCustomer Customer { get; set; }
}

/// <summary>Attributes for creating a Maxio customer (subset of the spec's <c>Create-Customer</c>).</summary>
public sealed class MaxioCreateCustomer
{
    public required string FirstName { get; set; }

    public required string LastName { get; set; }

    public required string Email { get; set; }

    /// <summary>Application-provided unique reference used for idempotent customer lookup.</summary>
    public required string Reference { get; set; }
}

/// <summary>
/// Request body for <c>POST /subscriptions.json</c> (createSubscription).
/// Mirrors <c>Create-Subscription-Request.yaml</c> / <c>Create-Subscription.yaml</c> from the Maxio spec.
/// </summary>
public sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscriptionRequest(MaxioCreateSubscription subscription)
    {
        Subscription = subscription;
    }

    public MaxioCreateSubscription Subscription { get; set; }
}

/// <summary>Attributes for creating a Maxio subscription (subset of the spec's <c>Create-Subscription</c>).</summary>
public sealed class MaxioCreateSubscription
{
    /// <summary>The API handle of the product to subscribe to (spec: required unless <c>product_id</c>).</summary>
    public string? ProductHandle { get; set; }

    /// <summary>The id of an existing Maxio customer (spec: required unless customer_reference/attributes).</summary>
    public long? CustomerId { get; set; }

    /// <summary>The application-provided unique reference for the subscription itself (spec: <c>reference</c>).</summary>
    public string? Reference { get; set; }

    /// <summary>
    /// How the subscription is collected (spec: <c>payment_collection_method</c>; enum from
    /// <c>Collection-Method.yaml</c>). 'remittance' lets a plan that does not require a credit card
    /// be subscribed to without a stored payment profile.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}
