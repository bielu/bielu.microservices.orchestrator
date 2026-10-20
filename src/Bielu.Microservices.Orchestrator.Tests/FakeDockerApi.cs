using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Handler.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// In-process stand-in for the Docker Engine API. A real <see cref="DockerClient"/> is built over it
/// through Docker.DotNet's pluggable transport, so tests exercise the client's actual URL building,
/// query strings, JSON (de)serialisation and status-code-to-exception mapping. Requests are matched
/// on method and path with the API version prefix removed (<c>/v1.47/containers/json</c> becomes
/// <c>/containers/json</c>); unrouted requests get a 404, like a missing resource.
/// </summary>
internal sealed partial class FakeDockerApi : HttpMessageHandler
{
    private readonly Dictionary<(HttpMethod Method, string Path), Func<DockerRequest, DockerResponse>> _routes = [];

    public List<DockerRequest> Requests { get; } = [];

    /// <summary>Creates a client whose every request is answered by this fake.</summary>
    public DockerClient CreateClient() =>
        new DockerClientBuilder()
            .WithEndpoint(new Uri("http://docker.test"))
            .WithTransportOptions(new Transport(this), this)
            .Build();

    public FakeDockerApi On(HttpMethod method, string path, object? body, HttpStatusCode status = HttpStatusCode.OK) =>
        On(method, path, _ => new DockerResponse(status, body));

    public FakeDockerApi On(HttpMethod method, string path, Func<DockerRequest, DockerResponse> respond)
    {
        _routes[(method, path)] = respond;
        return this;
    }

    /// <summary>Makes <paramref name="path"/> fail with <paramref name="status"/> and a Docker error body.</summary>
    public FakeDockerApi Fail(HttpMethod method, string path, HttpStatusCode status) =>
        On(method, path, new { message = $"fake {(int)status}" }, status);

    /// <summary>Returns the requests sent to <paramref name="path"/>, in order.</summary>
    public IReadOnlyList<DockerRequest> To(HttpMethod method, string path) =>
        Requests.Where(r => r.Method == method && r.Path == path).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = VersionPrefix().Replace(request.RequestUri!.AbsolutePath, "");
        var recorded = new DockerRequest(request.Method, Uri.UnescapeDataString(path), Uri.UnescapeDataString(request.RequestUri.Query), body);
        Requests.Add(recorded);

        var response = _routes.TryGetValue((request.Method, recorded.Path), out var respond)
            ? respond(recorded)
            : new DockerResponse(HttpStatusCode.NotFound, new { message = $"no route for {request.Method} {recorded.Path}" });

        var content = response.Body switch
        {
            null => new ByteArrayContent([]),
            byte[] bytes => new ByteArrayContent(bytes),
            string text => new StringContent(text, Encoding.UTF8),
            _ => new StringContent(JsonSerializer.Serialize(response.Body), Encoding.UTF8)
        };
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(response.ContentType);

        return new HttpResponseMessage(response.Status) { RequestMessage = request, Content = content };
    }

    /// <summary>Encodes <paramref name="text"/> as one stdout frame of Docker's multiplexed log stream.</summary>
    public static byte[] StdoutFrame(string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var frame = new byte[8 + payload.Length];
        frame[0] = 1;
        frame[4] = (byte)(payload.Length >> 24);
        frame[5] = (byte)(payload.Length >> 16);
        frame[6] = (byte)(payload.Length >> 8);
        frame[7] = (byte)payload.Length;
        payload.CopyTo(frame, 8);
        return frame;
    }

    [GeneratedRegex(@"^/v\d+(\.\d+)?")]
    private static partial Regex VersionPrefix();

    private sealed class Transport(FakeDockerApi api) : IDockerHandlerFactory<FakeDockerApi>
    {
        public ResolvedTransport CreateHandler(FakeDockerApi options, ClientOptions clientOptions, ILogger logger) =>
            new(options, new Uri("http://docker.test"));

        public ResolvedTransport CreateHandler(ClientOptions clientOptions, ILogger logger) =>
            CreateHandler(api, clientOptions, logger);

        // Only attach/exec hijack the connection; the orchestrator uses neither.
        public Task<WriteClosableStream> HijackStreamAsync(HttpContent content) =>
            throw new NotSupportedException();
    }
}

/// <summary>A request received by <see cref="FakeDockerApi"/>.</summary>
internal sealed record DockerRequest(HttpMethod Method, string Path, string Query, string? Body)
{
    /// <summary>The request body parsed as JSON.</summary>
    public JsonElement Json => JsonDocument.Parse(Body!).RootElement;
}

/// <summary>A response returned by <see cref="FakeDockerApi"/>.</summary>
internal sealed record DockerResponse(HttpStatusCode Status, object? Body, string ContentType = "application/json");
