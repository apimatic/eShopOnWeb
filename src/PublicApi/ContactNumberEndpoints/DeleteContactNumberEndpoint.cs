using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberRequest : BaseRequest
{
    public int ContactNumberId { get; set; }

    public DeleteContactNumberRequest(int contactNumberId)
    {
        ContactNumberId = contactNumberId;
    }
}

public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext httpContext, IContactNumberService contactNumberService) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), contactNumberService, httpContext);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService contactNumberService)
        => HandleAsync(request, contactNumberService, httpContext: null!);

    private async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService contactNumberService, HttpContext httpContext)
    {
        var buyerId = ShopperIdentity.TryGetBuyerId(httpContext);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        await contactNumberService.DeleteAsync(buyerId, request.ContactNumberId);
        return Results.NoContent();
    }
}
