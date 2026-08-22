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

public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, IOrderNotificationService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, service, user);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IOrderNotificationService service) =>
        await HandleAsync(request, service, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(CreateContactNumberRequest request, IOrderNotificationService service, ClaimsPrincipal user)
    {
        var unauthorized = user.RequireBuyerId(out var buyerId);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var created = await service.RegisterContactNumberAsync(buyerId, request.PhoneNumber);
        var response = new CreateContactNumberResponse
        {
            ContactNumberId = created.ContactNumberId,
            PhoneNumber = created.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}

public class CreateContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class CreateContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}
