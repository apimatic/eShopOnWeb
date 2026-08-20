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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, HttpContext httpContext, IContactNumberService service) =>
            {
                return await HandleAsync(request with { BuyerId = httpContext.GetRequiredBuyerId() }, service);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService contactNumberService)
    {
        var result = await contactNumberService.RegisterAsync(request.BuyerId!, request.PhoneNumber ?? string.Empty);
        return result.ToHttpResult(contact =>
        {
            var response = new RegisterContactNumberResponse
            {
                ContactNumberId = contact.Id,
                PhoneNumber = contact.PhoneNumber
            };
            return Results.Created($"api/contact-numbers/{contact.Id}", response);
        });
    }
}

public record RegisterContactNumberRequest
{
    public string? PhoneNumber { get; init; }
    internal string? BuyerId { get; init; }
}

public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}
