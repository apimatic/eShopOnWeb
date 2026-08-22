using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, ISavedCardService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId }, service, user);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedCardService service) =>
        HandleAsync(request, service, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedCardService service, ClaimsPrincipal user)
    {
        var buyerId = EndpointUser.RequireBuyerId(user);
        await service.DeleteAsync(buyerId, request.PaymentMethodId);
        return Results.NoContent();
    }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
}
