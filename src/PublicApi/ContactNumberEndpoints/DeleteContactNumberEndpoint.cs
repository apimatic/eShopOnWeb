using System.Threading;
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
            (int contactNumberId, HttpContext httpContext, IContactNumberService service, CancellationToken ct) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), service, httpContext, ct);
            })
            .Produces<DeleteContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service)
        => HandleAsync(request, service, null!, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        DeleteContactNumberRequest request,
        IContactNumberService service,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var buyerId = EndpointIdentity.GetBuyerId(httpContext);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        await service.DeleteAsync(buyerId, request.ContactNumberId, ct);
        return Results.Ok(new DeleteContactNumberResponse(request.CorrelationId()));
    }
}
