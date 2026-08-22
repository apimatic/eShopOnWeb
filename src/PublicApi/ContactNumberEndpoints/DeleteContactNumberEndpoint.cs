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
                    new DeleteContactNumberRequest(contactNumberId, httpContext.GetBuyerId()),
                    contactNumberService);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService contactNumberService)
    {
        await contactNumberService.DeleteAsync(request.BuyerId, request.ContactNumberId);
        return Results.NoContent();
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
    public string BuyerId { get; }
}
