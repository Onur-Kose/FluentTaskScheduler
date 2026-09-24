using FluentTaskScheduler.Extensions;
using FluentTaskScheduler.UI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace FluentTaskScheduler.Tests;

public class DashboardMappingTests
{
    [Xunit.Theory]
    [Xunit.InlineData("Development", true, 2)]
    [Xunit.InlineData("Production", true, 0)]
    [Xunit.InlineData("Production", false, 2)]
    public void RoutesRespectEnvironment(string environment, bool onlyInDevelopment, int expectedCount)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Services.AddFluentTaskScheduler();
        using var app = builder.Build();

        app.MapFluentTaskSchedulerDashboard(onlyInDevelopment: onlyInDevelopment);

        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty).ToArray();
        Xunit.Assert.Equal(expectedCount, routes.Length);
        if (expectedCount == 2)
            Xunit.Assert.Equal(new[] { "/scheduler", "/scheduler/api/status" }, routes);
    }
}
