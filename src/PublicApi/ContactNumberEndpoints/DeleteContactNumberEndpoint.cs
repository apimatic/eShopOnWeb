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
    public string BuyerId { get; set; } = string.Empty;
}

public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IShopperContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int contactNumberId, HttpContext httpContext, IShopperContactNumberService contactNumbers) =>
            {
                var request = new DeleteContactNumberRequest
                {
                    ContactNumberId = contactNumberId,
                    BuyerId = httpContext.GetRequiredBuyerId()
                };
                return await HandleAsync(request, contactNumbers);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IShopperContactNumberService contactNumbers)
    {
        await contactNumbers.DeleteAsync(request.BuyerId, request.ContactNumberId);
        return Results.NoContent();
    }
}
