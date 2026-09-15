using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TraceRag.Api;
using TraceRag.Core;

namespace TraceRag.Tests;

public sealed class ApiEndpointTests
{
    [Fact]
    public async Task HealthReturnsServiceState()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"healthy\"", body);
        Assert.Contains("\"storage\":\"in-memory\"", body);
    }

    [Fact]
    public async Task DocumentCanBeIngestedThenAskedWithEvidence()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var ingestResponse = await client.PostAsJsonAsync(
            "/api/documents",
            new CreateDocumentRequest(
                "local-guide",
                "Local guide",
                "The local API listens on port 5080 during the sample run."));

        Assert.Equal(HttpStatusCode.Created, ingestResponse.StatusCode);

        var askResponse = await client.PostAsJsonAsync(
            "/api/ask",
            new AskRequest("Which port does the local API use?", MaxResults: 3));
        var result = await askResponse.Content.ReadFromJsonAsync<AskResult>();

        Assert.Equal(HttpStatusCode.OK, askResponse.StatusCode);
        Assert.NotNull(result);
        Assert.True(result.HasSufficientEvidence);
        Assert.Contains("5080", result.Answer);
        Assert.Equal("local-guide", Assert.Single(result.Citations).DocumentId);
        Assert.True(result.SelfCheck.Passed);
    }

    [Fact]
    public async Task InvalidDocumentReturnsValidationProblem()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/documents",
            new CreateDocumentRequest("invalid id!", "", ""));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"id\"", body);
        Assert.Contains("\"title\"", body);
        Assert.Contains("\"content\"", body);
    }

    [Fact]
    public async Task OpenApiDocumentListsPublicEndpoints()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/health", body);
        Assert.Contains("/api/documents", body);
        Assert.Contains("/api/ask", body);
    }
}
