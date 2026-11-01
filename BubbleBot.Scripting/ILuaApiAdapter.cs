using MoonSharp.Interpreter;

namespace BubbleBot.Scripting;

/// <summary>
/// Registers one explicitly-scoped API surface in a script instance.
/// Implementations must expose only documented operations and must not
/// report success for operations they do not implement.
/// </summary>
public interface ILuaApiAdapter
{
    string Name { get; }

    void Register(Script script);
}
