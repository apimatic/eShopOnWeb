using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberEndpoint : IEndpoint<IResult, IShopperContactService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IShopperContactService contactService, HttpContext httpContext) =>
            {
                return await HandleAsync(contactNumberId, contactService, httpContext);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(IShopperContactService contactService)
        => Task.FromResult(Results.BadRequest());

    private async Task<IResult> HandleAsync(int contactNumberId, IShopperContactService contactService, HttpContext httpContext)
    {
        await contactService.DeleteAsync(httpContext.GetRequiredBuyerId(), contactNumberId);
        return Results.NoContent();
    }
}
