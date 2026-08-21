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
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RegisterContactNumberEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, IContactNumberService contactNumbers) =>
            {
                return await HandleAsync(request, contactNumbers);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService contactNumbers)
    {
        var buyerId = HttpContextBuyer.GetBuyerId(_httpContextAccessor.HttpContext!);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "phoneNumber is required." });
        }

        var contact = await contactNumbers.RegisterAsync(buyerId, request.PhoneNumber);
        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contact.Id,
            PhoneNumber = contact.CanonicalNumber
        };

        return Results.Created($"api/contact-numbers/{contact.Id}", response);
    }
}
