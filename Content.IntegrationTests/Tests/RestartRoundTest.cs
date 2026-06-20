using Content.Server.GameTicking;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests
{
    [TestFixture]
    public sealed class RestartRoundTest
    {
        [Test]
        public async Task RestartRoundTestRun()
        {
            await using var pair = await PoolManager.GetServerClient(new PoolSettings
            {
                DummyTicker = true,
                Connected = true,
                Dirty = true,
                Map = "Empty",
                Fresh = true,
                Destructive = true //HL: Messing with round state breaks future tests
            });
            var server = pair.Server;
            var sysManager = server.ResolveDependency<IEntitySystemManager>();

            await server.WaitPost(() =>
            {
                sysManager.GetEntitySystem<GameTicker>().RestartRound();
            });

            await pair.RunTicksSync(10);
            await pair.CleanReturnAsync();
        }
    }
}
