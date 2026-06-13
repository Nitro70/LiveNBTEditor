using Xunit;

namespace LiveNBT.Tests;

public class ForgetTest
{
    [Fact]
    public void ForgottenRequestIsNoLongerMatched()
    {
        var router = new LiveNBT.Protocol.MessageRouter();
        var (id, task) = router.NewRequest();
        router.Forget(id);
        router.OnIncoming($$"""{"id":{{id}},"ok":true}""");   // reply now lands as unmatched, not on the task
        Assert.False(task.IsCompleted);
    }
}