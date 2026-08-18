using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>
/// POST /api/contact-numbers — registers a mobile number for the signed-in shopper. A number the provider
/// does not consider a usable destination is rejected here; the provider's canonical form is stored.
/// </summary>
public class RegisterContactNumberEndpoint
    : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, IContactNumberService service, HttpContext http) =>
                await HandleAsync(request, service, http))
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service, HttpContext http)
    {
        var buyerId = http.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request is null || string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { error = "A phone number is required." });
        }

        var result = await service.RegisterAsync(buyerId, request.PhoneNumber, http.RequestAborted);
        if (!result.Success)
        {
            return Results.BadRequest(new { error = result.Error });
        }

        var response = new RegisterContactNumberResponse
        {
            ContactNumberId = result.ContactNumberId,
            PhoneNumber = result.CanonicalNumber!
        };
        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}
