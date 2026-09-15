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
            (int contactNumberId, HttpContext http, IContactNumberService service, CancellationToken cancellationToken) =>
            {
                var userName = http.GetUserName();
                if (string.IsNullOrEmpty(userName))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), service, userName, cancellationToken);
            })
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service)
        => HandleAsync(request, service, string.Empty, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        DeleteContactNumberRequest request,
        IContactNumberService service,
        string buyerId,
        CancellationToken cancellationToken)
    {
        var deleted = await service.DeleteAsync(buyerId, request.ContactNumberId, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}

public class DeleteContactNumberRequest : BaseRequest
{
    public DeleteContactNumberRequest(int contactNumberId)
    {
        ContactNumberId = contactNumberId;
    }

    public int ContactNumberId { get; }
}
