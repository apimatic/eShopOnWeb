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

public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int contactNumberId, HttpContext http, IContactNumberService service) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest
                {
                    ContactNumberId = contactNumberId,
                    BuyerId = http.User.GetBuyerId()
                }, service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service)
    {
        var deleted = await service.DeleteAsync(request.BuyerId, request.ContactNumberId);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}

public class DeleteContactNumberRequest : BaseRequest
{
    public int ContactNumberId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}
