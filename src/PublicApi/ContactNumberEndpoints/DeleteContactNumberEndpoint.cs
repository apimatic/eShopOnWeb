using System.Security.Claims;
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

/// <summary>
/// Removes one of the signed-in shopper's contact numbers. Afterwards it no longer appears among their
/// numbers and nothing is sent to it again. A shopper may only delete their own number.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest>
{
    private readonly IOrderNotificationService _service;

    public DeleteContactNumberEndpoint(IOrderNotificationService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest { ContactNumberId = contactNumberId, CallerId = user.GetUserId() }, ct);
            })
            .Produces<DeleteContactNumberResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(DeleteContactNumberRequest request) => HandleAsync(request, default);

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, CancellationToken ct)
    {
        var response = new DeleteContactNumberResponse(request.CorrelationId());
        if (string.IsNullOrEmpty(request.CallerId)) return Results.Unauthorized();

        var deleted = await _service.DeleteContactNumberAsync(request.CallerId, request.ContactNumberId, ct);
        return deleted ? Results.Ok(response) : Results.NotFound();
    }
}
