using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace O11yPartyBuzzer.Tests;

public class ChaosProtectionTests
{
    [Fact]
    public async Task Production_rejects_chaos_query_parameter()
    {
        await using var factory = new O11yPartyBuzzerApplicationFactory("Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/?chaos=exception");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Chaos engineering disabled", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Production_allows_requests_without_chaos_query_parameter()
    {
        await using var factory = new O11yPartyBuzzerApplicationFactory("Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("O11yParty-Buzzer", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Development_allows_chaos_query_parameter()
    {
        await using var factory = new O11yPartyBuzzerApplicationFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/?chaos=exception");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("O11yParty-Buzzer", await response.Content.ReadAsStringAsync());
    }

    private sealed class O11yPartyBuzzerApplicationFactory(string environmentName) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environmentName);
        }
    }
}
