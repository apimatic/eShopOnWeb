using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest
{
    public int PaymentMethodId { get; set; }
}

public class SavePaymentMethodRequest : CardPaymentRequest
{
}

public class PaymentMethodResponse : BaseResponse
{
    public PaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public PaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? CardType { get; set; }
}

public class PaymentMethodListResponse : BaseResponse
{
    public PaymentMethodListResponse(Guid correlationId) : base(correlationId) { }
    public PaymentMethodListResponse() { }

    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public DeletePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string Status { get; set; } = string.Empty;
}
