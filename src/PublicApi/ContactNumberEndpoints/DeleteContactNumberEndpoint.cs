using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IRepository<BuyerContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IRepository<BuyerContactNumber> repository, HttpContext httpContext) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId, httpContext.User.GetBuyerId()), repository);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IRepository<BuyerContactNumber> repository)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var entity = await repository.GetByIdAsync(request.ContactNumberId);
        if (entity == null || entity.BuyerId != request.BuyerId)
        {
            return Results.NotFound();
        }

        await repository.DeleteAsync(entity);
        return Results.NoContent();
    }
}

public record DeleteContactNumberRequest(int ContactNumberId, string? BuyerId);
