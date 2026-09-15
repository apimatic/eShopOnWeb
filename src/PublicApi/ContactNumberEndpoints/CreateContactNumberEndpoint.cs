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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateContactNumberRequest request, HttpContext httpContext, IContactNumberService service) =>
            {
                return await HandleAsync(request, service, httpContext);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService service)
    {
        return HandleAsync(request, service, null!);
    }

    private async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService service, HttpContext httpContext)
    {
        var buyerId = httpContext.User.RequireBuyerId();
        var contact = await service.RegisterAsync(buyerId, request.PhoneNumber);
        var response = new CreateContactNumberResponse
        {
            ContactNumberId = contact.Id,
            PhoneNumber = contact.CanonicalNumber
        };
        return Results.Created($"api/contact-numbers/{contact.Id}", response);
    }
}
