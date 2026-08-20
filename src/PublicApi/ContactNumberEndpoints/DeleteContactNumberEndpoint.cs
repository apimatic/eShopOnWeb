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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext httpContext, IContactNumberService service) =>
            {
                var unauthorized = httpContext.UnauthorizedIfAnonymous();
                if (unauthorized is not null) return unauthorized;
                return await HandleAsync(contactNumberId, service, httpContext.GetBuyerId()!);
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(int contactNumberId, IContactNumberService service) =>
        HandleAsync(contactNumberId, service, string.Empty);

    private async Task<IResult> HandleAsync(int contactNumberId, IContactNumberService service, string buyerId)
    {
        await service.DeleteAsync(buyerId, contactNumberId, default);
        return Results.Ok();
    }
}
