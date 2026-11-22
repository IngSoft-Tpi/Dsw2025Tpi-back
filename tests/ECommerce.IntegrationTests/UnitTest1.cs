using System.Net;

namespace ECommerce.IntegrationTests;

[Collection("IntegrationTests")]
public class UnitTest1(CustomWebApplicationFactory factory)
{
    [Fact]
    public async Task HealthCheckReturnsOk()
    {
        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.GetAsync("/healthcheck");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}