namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Request bodies mapping to the Maxio Advanced Billing OpenAPI schemas. Null properties are omitted on
// serialization (see MaxioClient's JsonSerializerOptions), so only the intended fields are sent.

/// <summary>Body for POST /customers.json: <c>{ "customer": { ... } }</c>. See schema Create-Customer-Request.</summary>
public record CreateCustomerRequest(CreateCustomerBody Customer);

/// <summary>Create Customer attributes. See schema Create-Customer (first_name, last_name, email required).</summary>
public record CreateCustomerBody(
    string FirstName,
    string LastName,
    string Email,
    string? Reference);

/// <summary>Body for POST /subscriptions.json: <c>{ "subscription": { ... } }</c>. See schema Create-Subscription-Request.</summary>
public record CreateSubscriptionRequest(CreateSubscriptionBody Subscription);

/// <summary>Create Subscription attributes. See schema Create-Subscription.</summary>
public record CreateSubscriptionBody(
    string ProductHandle,
    int CustomerId,
    string PaymentCollectionMethod);
