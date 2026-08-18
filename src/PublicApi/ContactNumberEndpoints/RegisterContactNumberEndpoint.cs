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

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a
/// usable destination is rejected here; the provider's canonical form is what gets stored.
/// </summary>
public class RegisterContactNumberEndpoint
    : IEndpoint<IResult, RegisterContactNumberRequest, string, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IContactNumberService service) =>
            {
                return await HandleAsync(request, user.Identity!.Name!, service);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, string buyerId,
        IContactNumberService service)
    {
        var result = await service.RegisterAsync(buyerId, request.PhoneNumber);
        if (!result.IsSuccess)
            return result.ToFailureResult();

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = result.Value.Id,
            PhoneNumber = result.Value.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}
