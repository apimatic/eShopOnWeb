using System.Security.Claims;
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
            (CreateContactNumberRequest request, IContactNumberService service, ClaimsPrincipal user) =>
            {
                var unauthorized = HttpCaller.RequireBuyerId(user, out var buyerId);
                if (unauthorized is not null)
                {
                    return unauthorized;
                }

                return await HandleAsync(request, service, buyerId);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService service)
        => HandleAsync(request, service, string.Empty);

    private async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService service, string buyerId)
    {
        var response = new CreateContactNumberResponse(request.CorrelationId());
        var contactNumber = await service.RegisterAsync(buyerId, request.PhoneNumber, default);
        response.ContactNumberId = contactNumber.Id;
        response.CanonicalNumber = contactNumber.CanonicalNumber;
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
