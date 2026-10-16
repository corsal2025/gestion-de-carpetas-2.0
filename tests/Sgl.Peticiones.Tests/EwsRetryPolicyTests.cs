using System.Net;
using PeticionCambioDomicilio.Ews;

namespace PeticionCambioDomicilio.Tests;

public class EwsRetryPolicyTests
{
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]      // 408
    [InlineData(HttpStatusCode.TooManyRequests)]     // 429
    [InlineData(HttpStatusCode.InternalServerError)] // 500
    [InlineData(HttpStatusCode.BadGateway)]          // 502
    [InlineData(HttpStatusCode.ServiceUnavailable)]  // 503
    [InlineData(HttpStatusCode.GatewayTimeout)]      // 504
    public void Transient_codes_retry(HttpStatusCode status)
    {
        Assert.True(EwsRetryPolicy.IsTransient(status));
        Assert.Equal(EwsAction.Retry, EwsRetryPolicy.Classify(status));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)] // 401
    [InlineData(HttpStatusCode.Forbidden)]    // 403
    public void Auth_failures_never_retry(HttpStatusCode status)
    {
        Assert.True(EwsRetryPolicy.IsAuthFailure(status));
        Assert.False(EwsRetryPolicy.IsTransient(status));
        Assert.Equal(EwsAction.FailAuth, EwsRetryPolicy.Classify(status));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]           // 400
    [InlineData(HttpStatusCode.NotFound)]             // 404
    [InlineData(HttpStatusCode.UnsupportedMediaType)] // 415
    public void Other_client_errors_fail_without_retry(HttpStatusCode status)
    {
        Assert.Equal(EwsAction.FailPermanent, EwsRetryPolicy.Classify(status));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Accepted)]
    public void Success_is_accepted(HttpStatusCode status)
    {
        Assert.Equal(EwsAction.Accept, EwsRetryPolicy.Classify(status));
    }
}
