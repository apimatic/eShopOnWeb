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
            async (CreateContactNumberRequest request, IContactNumberService service, HttpContext http) =>
            {
                return await HandleAsync(request, service, http);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService service)
        => HandleAsync(request, service, null!);

    private async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService service, HttpContext http)
    {
        var unauthorized = http.RequireBuyerId(out var buyerId);
        if (unauthorized != null)
        {
            return unauthorized;
        }

        try
        {
            var created = await service.RegisterAsync(buyerId, request.PhoneNumber);
            var response = new CreateContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = created.Id,
                PhoneNumber = created.CanonicalNumber
            };
            return Results.Created($"api/contact-numbers/{created.Id}", response);
        }
        catch (InvalidContactNumberException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}
