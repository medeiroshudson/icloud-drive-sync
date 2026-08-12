using System.Net;

namespace ICloudDriveSync.Tests.TestInfra;

/// <summary>HttpMessageHandler fake para testes de protocolo (sem rede).</summary>
public sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        return new HttpClient(handler) { BaseAddress = new Uri("https://setup.icloud.com/") };
    }

    public static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) => new()
    {
        StatusCode = status,
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(responder(request));
    }

    public IReadOnlyList<HttpRequestMessage> SentRequests => Requests;
}