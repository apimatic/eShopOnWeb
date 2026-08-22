using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, ISavedPaymentMethodService service, HttpContext httpContext) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest
                {
                    PaymentMethodId = paymentMethodId,
                    BuyerId = httpContext.RequireBuyerId()
                }, service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedPaymentMethodService service)
    {
        await service.DeleteAsync(request.BuyerId, request.PaymentMethodId);
        return Results.NoContent();
    }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}
