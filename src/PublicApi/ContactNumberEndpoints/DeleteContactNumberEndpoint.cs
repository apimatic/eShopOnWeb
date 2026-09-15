using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int contactNumberId, IContactNumberService contactNumberService, HttpContext httpContext) =>
            {
                return await HandleAsync(contactNumberId, contactNumberService, httpContext);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(int contactNumberId, IContactNumberService contactNumberService)
        => HandleAsync(contactNumberId, contactNumberService, null!);

    private async Task<IResult> HandleAsync(int contactNumberId, IContactNumberService contactNumberService, HttpContext httpContext)
    {
        var buyerId = httpContext.User.GetBuyerId();
        await contactNumberService.DeleteAsync(buyerId, contactNumberId);
        return Results.NoContent();
    }
}
