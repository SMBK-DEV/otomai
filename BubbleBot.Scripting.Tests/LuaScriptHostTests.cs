using BubbleBot.Scripting;
using Xunit;

namespace BubbleBot.Scripting.Tests;

public sealed class LuaScriptHostTests
{
    [Fact]
    public void LoadsAndInvokesLuaCallback()
    {
        using var host = new LuaScriptHost();
        var loaded = host.Load("counter = 0; function tick() counter = counter + 1; return counter end");

        Assert.True(loaded.Succeeded, loaded.Error);
        var first = host.Invoke("tick");
        var second = host.Invoke("tick");

        Assert.True(first.Succeeded, first.Error);
        Assert.True(second.Succeeded, second.Error);
        Assert.Equal(1d, first.ReturnValue!.Number);
        Assert.Equal(2d, second.ReturnValue!.Number);
    }

    [Fact]
    public void EachHostHasAnIndependentGlobalEnvironment()
    {
        using var first = new LuaScriptHost();
        using var second = new LuaScriptHost();

        Assert.True(first.Load("account_marker = 'first'").Succeeded);
        Assert.True(second.Load("account_marker = 'second'").Succeeded);

        Assert.Equal("first", first.GetGlobal("account_marker").String);
        Assert.Equal("second", second.GetGlobal("account_marker").String);
    }

    [Fact]
    public void MissingCallbackReturnsFailureInsteadOfSuccess()
    {
        using var host = new LuaScriptHost();
        Assert.True(host.Load("value = 42").Succeeded);

        var result = host.Invoke("move");

        Assert.False(result.Succeeded);
        Assert.Contains("move", result.Error);
    }

    [Fact]
    public void LuaExceptionIsReturnedAsFailure()
    {
        using var host = new LuaScriptHost();
        Assert.True(host.Load("function fail() error('expected failure') end").Succeeded);

        var result = host.Invoke("fail");

        Assert.False(result.Succeeded);
        Assert.Contains("expected failure", result.Error);
    }
}
