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

public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int contactNumberId, ClaimsPrincipal user, IContactNumberService contactNumbers) =>
            {
                return await HandleAsync(contactNumberId, user, contactNumbers);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(int contactNumberId, IContactNumberService contactNumbers) =>
        HandleAsync(contactNumberId, new ClaimsPrincipal(), contactNumbers);

    private async Task<IResult> HandleAsync(int contactNumberId, ClaimsPrincipal user, IContactNumberService contactNumbers)
    {
        await contactNumbers.DeleteAsync(BuyerIdentity.RequireBuyerId(user), contactNumberId);
        return Results.NoContent();
    }
}
