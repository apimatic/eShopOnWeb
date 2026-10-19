using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The Maxio SDK brings System.Text.Json 10 into PublicApi, whose PipeWriter serialization requires
/// <c>PipeWriter.UnflushedBytes</c>. ASP.NET Core 8's TestServer response writer does not implement it
/// (Kestrel does), so in-process tests wrap the response body in a stream-backed feature that does.
/// </summary>
public class TestServerPipeWriterFix : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            var original = context.Features.Get<IHttpResponseBodyFeature>()!;
            var replacement = new StreamResponseBodyFeature(original.Stream, original);
            context.Features.Set<IHttpResponseBodyFeature>(replacement);
            try
            {
                await nextMiddleware();
                await replacement.CompleteAsync();
            }
            finally
            {
                context.Features.Set(original);
            }
        });
        next(app);
    };
}
