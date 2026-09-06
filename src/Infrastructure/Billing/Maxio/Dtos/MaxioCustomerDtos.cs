namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Dtos;

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    public long Id { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Reference { get; set; }
}

internal sealed class MaxioCreateCustomerRequest
{
    public required MaxioCreateCustomerAttributes Customer { get; init; }
}

internal sealed class MaxioCreateCustomerAttributes
{
    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required string Email { get; init; }

    public required string Reference { get; init; }
}
