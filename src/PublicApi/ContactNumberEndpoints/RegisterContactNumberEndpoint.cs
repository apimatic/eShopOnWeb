using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (RegisterContactNumberRequest request, HttpContext httpContext, IContactNumberService service) =>
            {
                return await HandleAsync(request, httpContext, service);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
        => HandleAsync(request, null!, service);

    private async Task<IResult> HandleAsync(RegisterContactNumberRequest request, HttpContext httpContext, IContactNumberService service)
    {
        var buyerId = AuthenticatedUser.GetBuyerId(httpContext.User);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var contact = await service.RegisterAsync(buyerId, request.PhoneNumber);
        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contact.Id,
            PhoneNumber = contact.CanonicalNumber
        };
        return Results.Created($"api/contact-numbers/{contact.Id}", response);
    }
}
