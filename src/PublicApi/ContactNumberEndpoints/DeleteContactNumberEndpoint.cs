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
            (int contactNumberId, HttpContext httpContext, IContactNumberService contactNumberService) =>
            {
                return await HandleAsync(
                    new DeleteContactNumberRequest(contactNumberId, httpContext.GetRequiredBuyerId()),
                    contactNumberService);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService contactNumberService)
    {
        var deleted = await contactNumberService.DeleteAsync(request.BuyerId, request.ContactNumberId);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}

public class DeleteContactNumberRequest : BaseRequest
{
    public DeleteContactNumberRequest(int contactNumberId, string buyerId)
    {
        ContactNumberId = contactNumberId;
        BuyerId = buyerId;
    }

    public int ContactNumberId { get; }
    internal string BuyerId { get; }
}
