using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}

public class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();
}

/// <summary>
/// Payload for creating a new Maxio customer.
/// </summary>
public class NewMaxioCustomer
{
    public NewMaxioCustomer(string firstName, string lastName, string email, string reference)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        Reference = reference;
    }

    [JsonPropertyName("first_name")]
    public string FirstName { get; }

    [JsonPropertyName("last_name")]
    public string LastName { get; }

    [JsonPropertyName("email")]
    public string Email { get; }

    [JsonPropertyName("reference")]
    public string Reference { get; }
}

public class NewMaxioCustomerEnvelope
{
    public NewMaxioCustomerEnvelope(NewMaxioCustomer customer)
    {
        Customer = customer;
    }

    [JsonPropertyName("customer")]
    public NewMaxioCustomer Customer { get; }
}
