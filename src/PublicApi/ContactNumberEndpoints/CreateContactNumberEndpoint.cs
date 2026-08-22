using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateContactNumberRequest request, HttpContext httpContext, IContactNumberService service) =>
            {
                return await HandleAsync(request, httpContext, service);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService service)
        => HandleAsync(request, null!, service);

    private async Task<IResult> HandleAsync(CreateContactNumberRequest request, HttpContext httpContext, IContactNumberService service)
    {
        var buyerId = BuyerId(httpContext);
        if (buyerId == null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var created = await service.RegisterAsync(buyerId, request.PhoneNumber);
            var response = new CreateContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = created.Id,
                PhoneNumber = created.PhoneNumber
            };
            return Results.Created($"api/contact-numbers/{created.Id}", response);
        }
        catch (UnusableDestinationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (DuplicateException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }

    private static string? BuyerId(HttpContext httpContext)
        => httpContext.User.Identity?.Name ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
}
