using LiveNBT.Protocol;
using Xunit;


namespace LiveNBT.Tests;

public class MessageRouterTests
{
    [Fact]
    public void ReplyResolvesPendingRequest()
    {
        var router = new MessageRouter();
        var (id, task) = router.NewRequest();
        router.OnIncoming($$"""{"id":{{id}},"ok":true}""");
        Assert.True(task.IsCompletedSuccessfully);
        Assert.True(task.Result.Ok);
    }

    [Fact]
    public void ErrorReplyCarriesMessage()
    {
        var router = new MessageRouter();
        var (id, task) = router.NewRequest();
        router.OnIncoming($$"""{"id":{{id}},"ok":false,"error":"boom"}""");
        Assert.False(task.Result.Ok);
        Assert.Equal("boom", task.Result.Error);
    }

    [Fact]
    public void UpdateFiresEventWithParsedValue()
    {
        var router = new MessageRouter();
        ServerMessage? seen = null;
        router.UpdateReceived += m => seen = m;
        router.OnIncoming("""{"op":"update","root":"player:Bob","path":"Pos","value":{"t":"int","v":5}}""");
        Assert.NotNull(seen);
        Assert.Equal("player:Bob", seen!.Root);
        Assert.Equal("Pos", seen.Path);
        Assert.Equal("5", seen.Value!.Scalar);
    }

    [Fact]
    public void NullUpdateValueMeansGone()
    {
        var router = new MessageRouter();
        ServerMessage? seen = null;
        router.UpdateReceived += m => seen = m;
        router.OnIncoming("""{"op":"update","root":"player:Bob","path":"Pos","value":null}""");
        Assert.Null(seen!.Value);
    }

    [Fact]
    public void HelloFiresHelloEvent()
    {
        var router = new MessageRouter();
        bool hello = false;
        router.HelloReceived += _ => hello = true;
        router.OnIncoming("""{"op":"hello","protocol":1,"authRequired":true}""");
        Assert.True(hello);
    }

    [Fact]
    public void UnknownIdIsIgnored_FailAllFaultsPending()
    {
        var router = new MessageRouter();
        router.OnIncoming("""{"id":999,"ok":true}"""); // no pending: no throw
        var (_, task) = router.NewRequest();
        router.FailAll(new InvalidOperationException("closed"));
        Assert.True(task.IsFaulted);
    }

    [Fact]
    public void RootsReplyExposesRawValue()
    {
        var msg = Wire.Parse("""{"id":2,"ok":true,"value":{"players":["Bob"],"worlds":["minecraft:overworld"]}}""");
        Assert.True(msg.Ok);
        Assert.Null(msg.Value);
        Assert.Contains("minecraft:overworld", msg.RawValue);
    }

    [Fact]
    public void BuildRequestProducesProtocolJson()
    {
        string json = Wire.BuildRequest(3, "set", "player:Bob", "Health",
            new NbtNode(NbtType.Float) { Scalar = "10" });
        Assert.Equal("""{"id":3,"op":"set","root":"player:Bob","path":"Health","value":{"t":"float","v":10}}""", json);
        string auth = Wire.BuildRequest(1, "auth", token: "abc");
        Assert.Equal("""{"id":1,"op":"auth","token":"abc"}""", auth);
    }

    [Fact]
    public void MalformedServerFrameThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Wire.Parse("not json"));
        Assert.Throws<FormatException>(() => Wire.Parse("[1,2]"));
    }

    [Fact]
    public void CorruptTypedNodeValueThrows()
    {
        Assert.Throws<FormatException>(() =>
            Wire.Parse("""{"id":3,"ok":true,"value":{"t":"int","v":"garbage"}}"""));
    }

    [Fact]
    public void NewRequestAfterFailAllFailsImmediately()
    {
        var router = new MessageRouter();
        router.FailAll(new InvalidOperationException("closed"));
        var (_, task) = router.NewRequest();
        Assert.True(task.IsFaulted);
    }

    [Fact]
    public void NonIntegralIdTreatedAsAbsent()
    {
        var msg = Wire.Parse("""{"id":1.5,"ok":true}""");
        Assert.Null(msg.Id);
    }

    [Fact]
    public void UnmatchedErrorReplyRaisesNotice()
    {
        var router = new MessageRouter();
        ServerMessage? seen = null;
        router.UnmatchedReply += m => seen = m;
        router.OnIncoming("""{"id":-1,"ok":false,"error":"request too large"}""");
        Assert.Equal("request too large", seen!.Error);
    }
}
