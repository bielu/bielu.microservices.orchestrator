using Grpc.Core;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// In-process stand-in for the containerd gRPC API. The generated clients are constructed over it,
/// so tests see real request messages and headers. Calls are answered by handlers registered per
/// method, and recorded. Unregistered calls fail with <see cref="StatusCode.Unimplemented"/>.
/// Like <c>Grpc.Net.Client</c> with its default options, a cancelled call fails with
/// <see cref="StatusCode.Cancelled"/> rather than <see cref="OperationCanceledException"/>.
/// </summary>
internal sealed class FakeContainerd : CallInvoker
{
    public const string Containers = "containerd.services.containers.v1.Containers";
    public const string Tasks = "containerd.services.tasks.v1.Tasks";
    public const string Images = "containerd.services.images.v1.Images";
    public const string Snapshots = "containerd.services.snapshots.v1.Snapshots";

    private readonly Dictionary<string, Func<object, CancellationToken, Task<object>>> _unary = [];
    private readonly Dictionary<string, Func<object, IEnumerable<object>>> _streaming = [];

    public List<GrpcCall> Calls { get; } = [];

    public FakeContainerd On<TRequest, TResponse>(string service, string method, Func<TRequest, TResponse> respond) =>
        OnAsync<TRequest, TResponse>(service, method, (request, _) => Task.FromResult(respond(request)));

    public FakeContainerd OnAsync<TRequest, TResponse>(string service, string method, Func<TRequest, CancellationToken, Task<TResponse>> respond)
    {
        _unary[$"/{service}/{method}"] = async (request, token) => (await respond((TRequest)request, token))!;
        return this;
    }

    public FakeContainerd OnStream<TRequest, TResponse>(string service, string method, Func<TRequest, IEnumerable<TResponse>> respond)
    {
        _streaming[$"/{service}/{method}"] = request => respond((TRequest)request).Cast<object>();
        return this;
    }

    /// <summary>Makes <paramref name="method"/> fail with <paramref name="code"/>.</summary>
    public FakeContainerd Fail(string service, string method, StatusCode code)
    {
        _unary[$"/{service}/{method}"] = (_, _) => Task.FromException<object>(new RpcException(new Status(code, $"fake {code}")));
        return this;
    }

    /// <summary>Returns the requests sent to <paramref name="method"/>, in order.</summary>
    public IReadOnlyList<T> Requests<T>(string service, string method) =>
        Calls.Where(c => c.Method == $"/{service}/{method}").Select(c => (T)c.Request).ToList();

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
        AsyncUnaryCall(method, host, options, request).ResponseAsync.GetAwaiter().GetResult();

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        Calls.Add(new GrpcCall(method.FullName, request, options.Headers));
        return new AsyncUnaryCall<TResponse>(
            InvokeAsync<TResponse>(method.FullName, request, options.CancellationToken),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => [],
            () => { });
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        Calls.Add(new GrpcCall(method.FullName, request, options.Headers));
        if (!_streaming.TryGetValue(method.FullName, out var respond))
        {
            throw new RpcException(new Status(StatusCode.Unimplemented, method.FullName));
        }

        return new AsyncServerStreamingCall<TResponse>(
            new StreamReader<TResponse>(respond(request).Cast<TResponse>()),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => [],
            () => { });
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
        throw new NotSupportedException();

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
        throw new NotSupportedException();

    private async Task<TResponse> InvokeAsync<TResponse>(string method, object request, CancellationToken token)
    {
        if (!_unary.TryGetValue(method, out var respond))
        {
            throw new RpcException(new Status(StatusCode.Unimplemented, method));
        }

        try
        {
            return (TResponse)await respond(request, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.Cancelled, "Call canceled by the client."));
        }
    }

    private sealed class StreamReader<T>(IEnumerable<T> items) : IAsyncStreamReader<T>
    {
        private readonly IEnumerator<T> _enumerator = items.GetEnumerator();

        public T Current => _enumerator.Current;

        public Task<bool> MoveNext(CancellationToken cancellationToken) => Task.FromResult(_enumerator.MoveNext());
    }
}

/// <summary>A call received by <see cref="FakeContainerd"/>.</summary>
internal sealed record GrpcCall(string Method, object Request, Metadata? Headers)
{
    /// <summary>The <c>containerd-namespace</c> header the call carried, if any.</summary>
    public string? Namespace => Headers?.GetValue("containerd-namespace");
}
