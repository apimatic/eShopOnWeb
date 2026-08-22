using System.Security.Claims;
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
            (int contactNumberId, IContactNumberService service, ClaimsPrincipal user) =>
            {
                var unauthorized = HttpCaller.RequireBuyerId(user, out var buyerId);
                if (unauthorized is not null)
                {
                    return unauthorized;
                }

                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), service, buyerId);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service)
        => HandleAsync(request, service, string.Empty);

    private async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service, string buyerId)
    {
        await service.DeleteAsync(buyerId, request.ContactNumberId, default);
        return Results.NoContent();
    }
}
