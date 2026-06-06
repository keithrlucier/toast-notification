using System.Net;
using Xunit;

namespace ToastRevival.Api.Tests;

/// <summary>
/// DEVOPS-L1: Verify Swagger UI is not exposed in the Production environment.
/// The API's Program.cs gates UseSwagger/UseSwaggerUI behind IsDevelopment().
/// ApiTestFactory always sets ASPNETCORE_ENVIRONMENT=Production, so all tests
/// in the LoadCollection already run in Production — this test makes the guard
/// explicit and named so a future regression is immediately visible.
/// </summary>
[Collection(nameof(LoadCollection))]
public sealed class SwaggerProductionTests
{
    private readonly LoadFixture _load;

    public SwaggerProductionTests(LoadFixture load)
    {
        _load = load;
    }

    [Fact]
    public async Task Swagger_ReturnsNotFound_InProduction()
    {
        // ApiTestFactory forces ASPNETCORE_ENVIRONMENT=Production (see ApiTestFactory.CreateHost).
        // In Production the app.UseSwagger() / app.UseSwaggerUI() branches are skipped,
        // so both the JSON spec and the UI must return 404.
        using var http = _load.Factory.CreateClient();

        var specResponse = await http.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.NotFound, specResponse.StatusCode);

        var uiResponse = await http.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.NotFound, uiResponse.StatusCode);
    }
}
