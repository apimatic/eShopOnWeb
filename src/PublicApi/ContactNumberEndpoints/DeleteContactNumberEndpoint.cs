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
            (int contactNumberId, IContactNumberService contactNumbers, HttpContext httpContext) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), contactNumbers, httpContext);
            })
            .Produces<DeleteContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService contactNumbers)
        => HandleAsync(request, contactNumbers, null!);

    private async Task<IResult> HandleAsync(
        DeleteContactNumberRequest request,
        IContactNumberService contactNumbers,
        HttpContext httpContext)
    {
        var buyerId = httpContext.User.GetBuyerId();
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        await contactNumbers.DeleteAsync(buyerId, request.ContactNumberId, httpContext.RequestAborted);
        return Results.Ok(new DeleteContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = request.ContactNumberId
        });
    }
}
