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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int contactNumberId, HttpContext http, IContactNumberService contactNumbers) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), http, contactNumbers);
            })
            .Produces<DeleteContactNumberResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService contactNumbers)
        => HandleAsync(request, null!, contactNumbers);

    private async Task<IResult> HandleAsync(DeleteContactNumberRequest request, HttpContext http, IContactNumberService contactNumbers)
    {
        await contactNumbers.DeleteAsync(http.GetRequiredBuyerId(), request.ContactNumberId);
        return Results.Ok(new DeleteContactNumberResponse(request.CorrelationId()));
    }
}
