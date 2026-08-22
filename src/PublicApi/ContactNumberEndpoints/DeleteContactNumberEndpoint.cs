using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext httpContext, IContactNumberService service) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), httpContext, service);
            })
            .Produces<DeleteContactNumberResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service)
        => HandleAsync(request, null!, service);

    private async Task<IResult> HandleAsync(DeleteContactNumberRequest request, HttpContext httpContext, IContactNumberService service)
    {
        var buyerId = httpContext.BuyerId();
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        await service.DeleteAsync(buyerId, request.ContactNumberId, httpContext.RequestAborted);
        return Results.Ok(new DeleteContactNumberResponse(request.CorrelationId()));
    }
}
