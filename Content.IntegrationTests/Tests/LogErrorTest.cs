using System.Linq;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests;

public sealed class LogErrorTest
{
    /// <summary>
    ///     This test ensures that error logs cause tests to fail.
    /// </summary>
    [Test]
    public async Task TestLogErrorCausesTestFailure()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;

        var cfg = server.ResolveDependency<IConfigurationManager>();
        var serverLogmill = server.ResolveDependency<ILogManager>().RootSawmill;
        var clientLogmill = client.ResolveDependency<ILogManager>().RootSawmill;

        // Default cvar is properly configured
        Assert.That(cfg.GetCVar(RTCVars.FailureLogLevel), Is.EqualTo(LogLevel.Error));

        // Warnings don't cause tests to fail.
        await server.WaitPost(() => serverLogmill.Warning("test"));

        // But errors do
        await server.WaitPost(() => serverLogmill.Error("test"));
        await client.WaitPost(() => clientLogmill.Error("test"));

        // Ensure logs have been processed
        await server.WaitIdleAsync();
        await client.WaitIdleAsync();

        // Capture failing logs then clear them so PoolManager doesn't auto-fail on return.
        // The logs actually fail on pair cleanup rather than instantly, which we can't catch unless we do this
        var serverFails = pair.ServerLogHandler.FailingLogs.ToList();
        var clientFails = pair.ClientLogHandler.FailingLogs.ToList();
        pair.ServerLogHandler.ClearContext();
        pair.ClientLogHandler.ClearContext();

        await pair.CleanReturnAsync();

        Assert.That(serverFails.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(clientFails.Count, Is.GreaterThanOrEqualTo(1));
    }
}
