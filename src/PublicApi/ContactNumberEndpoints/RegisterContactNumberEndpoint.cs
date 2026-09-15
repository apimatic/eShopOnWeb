using System.Security.Claims;
using System.Threading;
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
/// Registers a mobile number for the signed-in shopper. The number is validated with the provider and its
/// canonical form is stored; an unusable destination is rejected here rather than when a message later fails.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IContactNumberService service) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces<ContactNumberValidationResponse>(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId)) return Results.Unauthorized();

        var registration = await service.RegisterAsync(request.BuyerId, request.PhoneNumber);
        if (!registration.Succeeded)
        {
            return Results.BadRequest(new ContactNumberValidationResponse(request.CorrelationId())
            {
                Errors = registration.Errors
            });
        }

        var contactNumber = registration.ContactNumber!;
        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
