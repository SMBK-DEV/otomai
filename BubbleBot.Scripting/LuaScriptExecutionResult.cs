using MoonSharp.Interpreter;

namespace BubbleBot.Scripting;

public sealed record LuaScriptExecutionResult(
    bool Succeeded,
    DynValue? ReturnValue,
    string? Error)
{
    public static LuaScriptExecutionResult Success(DynValue? value = null) =>
        new(true, value, null);

    public static LuaScriptExecutionResult Failure(Exception exception) =>
        new(false, null, exception.Message);
}
