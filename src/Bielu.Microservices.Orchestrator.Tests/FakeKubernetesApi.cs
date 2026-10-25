using System.Net;
using System.Text;
using k8s;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// In-process stand-in for the Kubernetes API server. It sits in the real <see cref="k8s.Kubernetes"/>
/// client's handler pipeline and answers requests itself, so tests exercise the client's actual URL
/// building and (de)serialisation. Unrouted requests get a 404, like a missing resource.
/// </summary>
internal sealed class FakeKubernetesApi : DelegatingHandler
{
    private readonly Dictionary<(HttpMethod Method, string Path), Func<RecordedRequest, (HttpStatusCode Status, object? Body)>> _routes = [];

    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Creates a client whose every request is answered by this fake.</summary>
    public IKubernetes CreateClient() =>
        new k8s.Kubernetes(new KubernetesClientConfiguration { Host = "http://kubernetes.test" }, this);

    public FakeKubernetesApi On(HttpMethod method, string path, object? body, HttpStatusCode status = HttpStatusCode.OK) =>
        On(method, path, _ => (status, body));

    public FakeKubernetesApi On(HttpMethod method, string path, Func<RecordedRequest, (HttpStatusCode Status, object? Body)> respond)
    {
        _routes[(method, path)] = respond;
        return this;
    }

    /// <summary>A Kubernetes <c>Status</c> body; <see cref="k8s.Models.V1Status"/> itself cannot be serialised.</summary>
    public static string Status(int code, string reason) =>
        $$"""{"kind":"Status","apiVersion":"v1","status":"Failure","code":{{code}},"reason":"{{reason}}"}""";

    /// <summary>Returns the single recorded request for <paramref name="method"/> and <paramref name="path"/>.</summary>
    public RecordedRequest Single(HttpMethod method, string path) =>
        Requests.Single(r => r.Method == method && r.Path == path);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath, Uri.UnescapeDataString(request.RequestUri.Query), body);
        Requests.Add(recorded);

        var (status, responseBody) = _routes.TryGetValue((request.Method, recorded.Path), out var respond)
            ? respond(recorded)
            : (HttpStatusCode.NotFound, Status(404, "NotFound"));

        var json = responseBody switch
        {
            null => "{}",
            string text => text,
            _ => KubernetesJson.Serialize(responseBody)
        };

        return new HttpResponseMessage(status)
        {
            RequestMessage = request,
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}

/// <summary>A request received by <see cref="FakeKubernetesApi"/>.</summary>
internal sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string? Body)
{
    /// <summary>Deserialises the request body as a Kubernetes model.</summary>
    public T BodyAs<T>() => KubernetesJson.Deserialize<T>(Body!);
}
