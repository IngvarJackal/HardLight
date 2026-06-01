// HardLight: structured debug logging for diagnosing shuttle-gun / projectile
// aiming and rendering issues.
//
// Writes human-readable, single-line, greppable records under the engine's
// UserData directory (which the client sandbox permits, unlike raw System.IO):
//   client: %APPDATA%/Space Station 14/data/hl_proj_debug/client.log
//   server: <server exe dir>/data/hl_proj_debug/server.log  (dev: bin/Content.Server/data/...)
// so the client and server traces of the SAME shot can be diffed side by side.
//
// Each process truncates its own file on the first write of a run, so every game
// launch starts from a clean log.
//
// Exception-safe and dependency-light: debug logging must never crash or stall
// the game. Remove the call sites (search for "ProjDebug.") once the
// projectile-rendering bugs are resolved.

using System;
using System.Numerics;
using Robust.Shared.ContentPack;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Shared._Mono.Debugging;

public static class ProjDebug
{
    private static readonly object Lock = new();

    private static string? _side;
    private static IWritableDirProvider? _userData;
    private static ResPath _path;
    private static bool _initialized;
    private static bool _failed;
    private static bool _truncated;

    /// <summary>
    /// "server" / "client" determined lazily from the network manager.
    /// </summary>
    private static string Side
    {
        get
        {
            if (_side != null)
                return _side;

            try
            {
                _side = IoCManager.Resolve<INetManager>().IsServer ? "server" : "client";
            }
            catch
            {
                _side = "unknown";
            }

            return _side;
        }
    }

    private static bool TryInit()
    {
        if (_initialized)
            return !_failed;

        _initialized = true;
        try
        {
            _userData = IoCManager.Resolve<IResourceManager>().UserData;
            var dir = new ResPath("/hl_proj_debug");
            _userData.CreateDir(dir);
            _path = dir / $"{Side}.log";
        }
        catch
        {
            _failed = true;
        }

        return !_failed;
    }

    /// <summary>
    /// Append a structured record. <paramref name="tag"/> is a dotted category
    /// (e.g. "fire.attempt", "shoot.server", "projectile.spawn") so logs can be
    /// filtered with a simple substring search.
    /// </summary>
    public static void Log(string tag, string message)
    {
        try
        {
            lock (Lock)
            {
                if (!TryInit() || _userData == null)
                    return;

                if (!_truncated)
                {
                    _userData.WriteAllText(_path, $"# session start {DateTime.Now:O} side={Side}\n");
                    _truncated = true;
                }

                _userData.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss.fff}] {tag,-22} {message}\n");
            }
        }
        catch
        {
            // Debug logging must never break the game.
        }
    }

    /// <summary>Compact vector formatter, e.g. (12.34,-5.6).</summary>
    public static string V(Vector2 v) => $"({v.X:0.###},{v.Y:0.###})";

    /// <summary>Compact angle formatter in degrees.</summary>
    public static string Deg(Angle a) => $"{a.Degrees:0.##}deg";
}
