using System.Net;

namespace PaperNexus.Tests;

// Answers every request a feed reader makes with a fixed reply, so a test can read a feed at an
// https address without the network. Gate, when set, holds each reply until it completes, so a
// test can cancel a request while it is in flight.
internal sealed class FeedHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;

    public FeedHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _body = body;
        _status = status;
    }

    public TaskCompletionSource? Gate { get; set; }

    public List<Uri> Requests { get; } = [];

    // A feed listing two pictures in the shape the default picture-finding rules read.
    public const string TwoPictures = """[{"imageUrl":"https://img.test/a.jpg","title":"Lake"},{"imageUrl":"https://img.test/b.jpg","title":"Hill"}]""";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
            Requests.Add(request.RequestUri);
        if (Gate is not null)
            await Gate.Task.WaitAsync(cancellationToken);
        var response = new HttpResponseMessage(_status) { Content = new StringContent(_body) };
        return response;
    }
}
