using System.Net;
using Echokraut.Services;
using Xunit;

namespace Echokraut.Tests;

public class JsonDataServiceTests
{
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]        // 408
    [InlineData(HttpStatusCode.TooManyRequests)]       // 429 — the reported startup failure
    [InlineData(HttpStatusCode.InternalServerError)]   // 500
    [InlineData(HttpStatusCode.BadGateway)]            // 502
    [InlineData(HttpStatusCode.ServiceUnavailable)]    // 503
    [InlineData(HttpStatusCode.GatewayTimeout)]        // 504
    public void IsTransient_TrueForRetryableStatuses(HttpStatusCode status)
    {
        Assert.True(JsonDataService.IsTransient(status));
    }

    [Fact]
    public void IsTransient_TrueForNullStatus_NetworkLevelFailure()
    {
        // A null status maps to a network-level failure (DNS/connection/timeout) — worth retrying.
        Assert.True(JsonDataService.IsTransient(null));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]                    // 200
    [InlineData(HttpStatusCode.NotFound)]              // 404
    [InlineData(HttpStatusCode.Forbidden)]             // 403
    [InlineData(HttpStatusCode.Unauthorized)]          // 401
    [InlineData(HttpStatusCode.BadRequest)]            // 400
    public void IsTransient_FalseForNonRetryableStatuses(HttpStatusCode status)
    {
        Assert.False(JsonDataService.IsTransient(status));
    }
}
