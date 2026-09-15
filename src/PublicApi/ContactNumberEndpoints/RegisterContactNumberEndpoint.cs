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
            async (RegisterContactNumberRequest request, IContactNumberService contactNumberService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, contactNumberService, httpContext);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService contactNumberService)
        => HandleAsync(request, contactNumberService, null!);

    private async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService contactNumberService, HttpContext httpContext)
    {
        var buyerId = httpContext.User.GetBuyerId();
        var created = await contactNumberService.RegisterAsync(buyerId, request.PhoneNumber);
        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = created.Id,
            PhoneNumber = created.CanonicalNumber
        };

        return Results.Created($"api/contact-numbers/{created.Id}", response);
    }
}
