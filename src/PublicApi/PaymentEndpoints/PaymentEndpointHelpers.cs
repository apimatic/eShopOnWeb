using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class PaymentEndpointHelpers
{
    public static string RequireBuyer(HttpContext context)
    {
        var name = context.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentException(401, "The caller is not authenticated.");
        }

        return name;
    }
}

public class OrderIdRequest
{
    public OrderIdRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; init; }
}

public class PayOrderCommand : PayOrderRequest
{
    public int OrderId { get; set; }
}

public class RefundOrderCommand : RefundOrderRequest
{
    public int OrderId { get; set; }
}

public class PaymentMethodIdRequest
{
    public PaymentMethodIdRequest(Guid paymentMethodId)
    {
        PaymentMethodId = paymentMethodId;
    }

    public Guid PaymentMethodId { get; init; }
}

public class ReconciliationRequest
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
}
