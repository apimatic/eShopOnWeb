using System.Collections.Generic;
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

public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest>
{
    private readonly IContactNumberService _contactNumbers;

    public DeleteContactNumberEndpoint(IContactNumberService contactNumbers)
    {
        _contactNumbers = contactNumbers;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext httpContext) =>
            {
                var unauthorized = HttpCaller.UnauthorizedIfAnonymous(httpContext);
                if (unauthorized is not null)
                {
                    return unauthorized;
                }

                return await HandleAsync(new DeleteContactNumberRequest
                {
                    ContactNumberId = contactNumberId,
                    BuyerId = HttpCaller.BuyerId(httpContext)!,
                    CancellationToken = httpContext.RequestAborted
                });
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request)
    {
        try
        {
            await _contactNumbers.DeleteAsync(request.BuyerId, request.ContactNumberId, request.CancellationToken);
            return Results.Ok(new { status = "Deleted" });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }
}

public class DeleteContactNumberRequest : BaseRequest
{
    public int ContactNumberId { get; set; }
    internal string BuyerId { get; set; } = string.Empty;
    internal CancellationToken CancellationToken { get; set; }
}
