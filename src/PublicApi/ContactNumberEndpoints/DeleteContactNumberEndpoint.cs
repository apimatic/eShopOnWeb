using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the shopper's own numbers. Afterwards it no longer appears among the caller's
/// numbers and nothing is ever sent to it again. Another shopper's number cannot be removed.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext httpContext, IContactNumberService service) =>
            {
                var request = new DeleteContactNumberRequest
                {
                    ContactNumberId = contactNumberId,
                    CallerId = CallerIdentity.Get(httpContext) ?? string.Empty
                };
                return await HandleAsync(request, service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service)
    {
        if (string.IsNullOrEmpty(request.CallerId))
            return Results.Unauthorized();

        var deleted = await service.DeleteAsync(request.CallerId, request.ContactNumberId);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}

public class DeleteContactNumberRequest : AuthenticatedRequest
{
    public int ContactNumberId { get; set; }
}
