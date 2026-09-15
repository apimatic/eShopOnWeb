using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

public sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioCustomer
{
    public int? Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public sealed class CreateMaxioCustomerRequest
{
    public CreateMaxioCustomerRequest(CreateMaxioCustomer customer)
    {
        Customer = customer;
    }

    public CreateMaxioCustomer Customer { get; set; }
}

public sealed class CreateMaxioCustomer
{
    public CreateMaxioCustomer(string firstName, string lastName, string email, string reference)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        Reference = reference;
    }

    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public string Reference { get; set; }
}

public sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioProduct
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioProductFamily
{
    public int? Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
}

public sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

public sealed class MaxioSubscription
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public string? Reference { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

public sealed class CreateMaxioSubscriptionRequest
{
    public CreateMaxioSubscriptionRequest(CreateMaxioSubscription subscription)
    {
        Subscription = subscription;
    }

    public CreateMaxioSubscription Subscription { get; set; }
}

public sealed class CreateMaxioSubscription
{
    public CreateMaxioSubscription(string productHandle, int customerId, string reference)
    {
        ProductHandle = productHandle;
        CustomerId = customerId;
        Reference = reference;
    }

    public string ProductHandle { get; set; }
    public int CustomerId { get; set; }
    public string Reference { get; set; }
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
