using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Request to save (vault) a card. Reuses the shared <see cref="CardDto"/> shape.</summary>
public class SavePaymentMethodRequestDto
{
    public CardDto Card { get; set; } = new();
}

/// <summary>A saved card described safely enough to recognise — never full card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class SavePaymentMethodResponseDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class ListPaymentMethodsResponseDto
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}
