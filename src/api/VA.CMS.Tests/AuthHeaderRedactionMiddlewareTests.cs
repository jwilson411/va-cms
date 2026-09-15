using Microsoft.AspNetCore.Http;
using VA.CMS.API.Middleware;

namespace VA.CMS.Tests;

/// <summary>
/// Unit tests for AuthHeaderRedactionMiddleware.
/// Verifies that the Authorization header value is replaced with [REDACTED]
/// so downstream logging middleware never writes bearer tokens to logs.
/// </summary>
public class AuthHeaderRedactionMiddlewareTests
{
    [Fact]
    public async Task Middleware_Redacts_Authorization_Header()
    {
        var context    = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = "Bearer eyJhbGciOiJIUzI1NiJ9.test.sig";

        // The middleware should redact before the next delegate sees the request.
        string? capturedValue = null;
        Task nextDelegate(HttpContext ctx)
        {
            capturedValue = ctx.Request.Headers["Authorization"].ToString();
            return Task.CompletedTask;
        }

        var middleware = new AuthHeaderRedactionMiddleware(nextDelegate);
        await middleware.InvokeAsync(context);

        Assert.Equal("[REDACTED]", capturedValue);
    }

    [Fact]
    public async Task Middleware_Does_Not_Alter_Other_Headers()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Custom-Header"] = "custom-value";
        context.Request.Headers["Authorization"]   = "Bearer sometoken";

        string? capturedCustom = null;
        Task nextDelegate(HttpContext ctx)
        {
            capturedCustom = ctx.Request.Headers["X-Custom-Header"].ToString();
            return Task.CompletedTask;
        }

        var middleware = new AuthHeaderRedactionMiddleware(nextDelegate);
        await middleware.InvokeAsync(context);

        Assert.Equal("custom-value", capturedCustom);
    }

    [Fact]
    public async Task Middleware_Passes_Request_Without_Authorization_Header_Unchanged()
    {
        var context = new DefaultHttpContext();
        // No Authorization header set.

        bool nextCalled = false;
        Task nextDelegate(HttpContext ctx)
        {
            nextCalled = true;
            return Task.CompletedTask;
        }

        var middleware = new AuthHeaderRedactionMiddleware(nextDelegate);
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.False(context.Request.Headers.ContainsKey("Authorization"));
    }
}
