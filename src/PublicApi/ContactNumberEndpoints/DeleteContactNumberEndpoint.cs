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
            (int contactNumberId, IContactNumberService contactNumberService, HttpContext httpContext) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), httpContext, contactNumberService);
            })
            .Produces<DeleteContactNumberResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService contactNumberService)
        => HandleAsync(request, null!, contactNumberService);

    private async Task<IResult> HandleAsync(
        DeleteContactNumberRequest request,
        HttpContext httpContext,
        IContactNumberService contactNumberService)
    {
        var response = new DeleteContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = request.ContactNumberId
        };

        await contactNumberService.DeleteAsync(
            httpContext.GetBuyerId(),
            request.ContactNumberId,
            httpContext.RequestAborted);

        return Results.Ok(response);
    }
}
