using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's numbers. Afterwards it no longer appears among their numbers
/// and nothing is ever sent to it again (order notifications only ever go to currently-registered numbers).
/// </summary>
public class DeleteContactNumberEndpoint : AuthenticatedEndpointBase,
    IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    public DeleteContactNumberEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
    {
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IContactNumberService service) =>
                await HandleAsync(new DeleteContactNumberRequest(contactNumberId), service))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service)
    {
        await service.DeleteAsync(BuyerId, request.ContactNumberId, RequestAborted);
        return Results.NoContent();
    }
}
