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

public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberEndpoint.Request, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (Request request, HttpContext http) => await HandleAsync(request, http))
            .Produces<Response>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, HttpContext http)
    {
        var buyerId = http.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var contact = await http.GetRequired<IContactNumberService>()
                .RegisterAsync(buyerId, request.PhoneNumber ?? string.Empty);
            var response = new Response
            {
                ContactNumberId = contact.Id,
                PhoneNumber = contact.CanonicalNumber
            };
            return Results.Created($"api/contact-numbers/{contact.Id}", response);
        }
        catch (InvalidContactNumberException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    public class Request
    {
        public string? PhoneNumber { get; set; }
    }

    public class Response
    {
        public int ContactNumberId { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
    }
}
