using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    public DeletePaymentMethodRequest(int paymentMethodId, string? buyerId)
    {
        PaymentMethodId = paymentMethodId;
        BuyerId = buyerId;
    }

    public int PaymentMethodId { get; set; }
    public string? BuyerId { get; set; }
}

/// <summary>Removes one of the signed-in shopper's saved cards, so it can no longer be seen or used to pay.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentMethodAppService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, HttpContext http, IPaymentMethodAppService service) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest(paymentMethodId, http.User.Identity?.Name), service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodAppService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var removed = await service.DeleteAsync(request.PaymentMethodId, request.BuyerId);
        return removed ? Results.NoContent() : Results.NotFound();
    }
}
