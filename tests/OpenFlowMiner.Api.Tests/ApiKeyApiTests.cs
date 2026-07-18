using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OpenFlowMiner.Api.Tests;

public class ApiKeyApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Post_ApiKeys_ReturnsKeyWithTierInfo()
    {
        var response = await _client.PostAsync("/api/v1/api-keys", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

        Assert.StartsWith("ofm_", body.GetProperty("key").GetString());
        Assert.Equal("X-Api-Key", body.GetProperty("header").GetString());
        Assert.True(body.GetProperty("rateLimit").GetProperty("permitLimit").GetInt32() > 100);
    }

    [Fact]
    public async Task AnonymousAccess_StillWorks_WithoutAnyKey()
    {
        // The whole point: keyless access is unchanged and never gated.
        var response = await _client.GetAsync("/api/v1/event-logs/receipt");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ValidKey_IsAccepted()
    {
        var created = await _client.PostAsync("/api/v1/api-keys", content: null);
        var key = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/event-logs/receipt");
        request.Headers.Add("X-Api-Key", key);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownKey_FallsThroughToAnonymous_StillSucceeds()
    {
        // An invalid/unknown key must not error or gate — it simply drops to the anonymous tier.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/event-logs/receipt");
        request.Headers.Add("X-Api-Key", "ofm_definitely_not_a_real_key");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
