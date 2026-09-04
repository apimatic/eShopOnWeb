using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; init; }

    public DeletePaymentMethodRequest(int paymentMethodId)
    {
        PaymentMethodId = paymentMethodId;
    }
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public bool Deleted { get; set; }
}

/// <summary>
/// Removes one of the signed-in shopper's saved cards; it is deleted from PayPal's
/// vault and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize] async (int paymentMethodId, IHttpContextAccessor httpContextAccessor, IPaymentService paymentService) =>
                await HandleAsync(paymentMethodId, httpContextAccessor, paymentService))
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(DeletePaymentMethodRequest request) => throw new NotSupportedException();

    public async Task<IResult> HandleAsync(int paymentMethodId, IHttpContextAccessor httpContextAccessor, IPaymentService paymentService)
    {
        var buyerId = httpContextAccessor.HttpContext.User.RequireBuyerId();

        await paymentService.DeletePaymentMethodAsync(buyerId, paymentMethodId);

        return Results.Ok(new DeletePaymentMethodResponse
        {
            PaymentMethodId = paymentMethodId,
            Deleted = true
        });
    }
}
