using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, HttpContext httpContext, IContactNumberService service) =>
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
        var buyerId = httpContext.BuyerId();
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var created = await service.RegisterAsync(buyerId, request.PhoneNumber, httpContext.RequestAborted);
        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = created.Id,
            PhoneNumber = created.CanonicalNumber
        };

        return Results.Created($"api/contact-numbers/{created.Id}", response);
    }
}
