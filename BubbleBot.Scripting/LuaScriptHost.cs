using MoonSharp.Interpreter;

namespace BubbleBot.Scripting;

/// <summary>
/// One host instance must be created per game account. The MoonSharp Script
/// object owns its own globals, so account state is not shared between hosts.
/// This host deliberately does not load files, open sockets, or expose the
/// operating system to Lua.
/// </summary>
public sealed class LuaScriptHost : IDisposable
{
    private readonly Script _script;
    private readonly object _gate = new();
    private bool _loaded;
    private bool _disposed;

    public LuaScriptHost(
        IEnumerable<ILuaApiAdapter>? adapters = null,
        Action<string>? log = null)
    {
        _script = new Script(CoreModules.Preset_SoftSandbox);
        _script.Options.DebugPrint = message => log?.Invoke(message);

        foreach (var adapter in adapters ?? [])
        {
            ArgumentNullException.ThrowIfNull(adapter);
            adapter.Register(_script);
        }
    }

    /// <summary>
    /// Loads and executes the script's top-level declarations once.
    /// Callbacks such as tick() can then be invoked separately.
    /// </summary>
    public LuaScriptExecutionResult Load(string source, string chunkName = "NYROX")
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_gate)
        {
            ThrowIfDisposed();

            try
            {
                var value = _script.DoString(source, null, chunkName);
                _loaded = true;
                return LuaScriptExecutionResult.Success(value);
            }
            catch (InterpreterException exception)
            {
                return LuaScriptExecutionResult.Failure(exception);
            }
            catch (Exception exception)
            {
                return LuaScriptExecutionResult.Failure(exception);
            }
        }
    }

    /// <summary>
    /// Invokes a previously declared Lua callback (for example tick or move).
    /// The caller is responsible for scheduling bounded callbacks and for
    /// cancelling the account lifecycle when its client disconnects.
    /// </summary>
    public LuaScriptExecutionResult Invoke(string callbackName, params DynValue[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackName);
        ArgumentNullException.ThrowIfNull(arguments);

        lock (_gate)
        {
            ThrowIfDisposed();

            if (!_loaded)
                return LuaScriptExecutionResult.Failure(
                    new InvalidOperationException("Lua script has not been loaded."));

            try
            {
                var callback = _script.Globals.Get(callbackName);
                if (callback.Type != DataType.Function)
                {
                    return LuaScriptExecutionResult.Failure(
                        new MissingMethodException($"Lua callback '{callbackName}' is not defined."));
                }

                return LuaScriptExecutionResult.Success(_script.Call(callback, arguments));
            }
            catch (InterpreterException exception)
            {
                return LuaScriptExecutionResult.Failure(exception);
            }
            catch (Exception exception)
            {
                return LuaScriptExecutionResult.Failure(exception);
            }
        }
    }

    /// <summary>
    /// Reads a global value for diagnostics and offline contract tests.
    /// </summary>
    public DynValue GetGlobal(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_gate)
        {
            ThrowIfDisposed();
            return _script.Globals.Get(name);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _script.Globals.Clear();
            _loaded = false;
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
