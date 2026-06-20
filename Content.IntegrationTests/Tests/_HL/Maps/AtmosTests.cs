using System.IO;
using System.Linq;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;
using Content.IntegrationTests.Tests._NF;
using Content.Shared.Atmos;

namespace Content.IntegrationTests.Tests._HL.Maps;

/// <summary>
/// Custom tests for the atmos on maps/.shuttles, currently only checks gas counts
/// </summary>
[TestFixture]
public sealed class AtmosTests
{

    private const bool SkipTestMaps = true;
    private const string TestMapsPath = "/Maps/_NF/Test/";
    private static readonly string[] GameMaps = FrontierConstants.GameMapPrototypes;

    /// <summary>
	/// Checks any GridAtmosphere on a shuttle and makes sure the amount of gasses matches what we have. Maps will fail to load if these don't match.
    /// We have to directly parse the .yml as trying to load the files in-engine throws errors all over the place
	/// </summary>
    [Test]
    public async Task TestAtmosMixGasCountShuttle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        int checkCount = 0;

        var resourceManager = server.ResolveDependency<IResourceManager>();
        // Get all the Shuttle files, there's probs a better way to do this but eh
        var mapFolder = new ResPath("/Maps");
        var maps = resourceManager
            .ContentFindFiles(mapFolder)
            .Where(filePath => filePath.Extension == "yml" && !filePath.Filename.StartsWith(".", StringComparison.Ordinal) && filePath.EnumerateSegments().Any(s => s == "Shuttles"))
            .ToArray();
        //maps = [new ResPath("/Maps/_Mono/Shuttles/World/ramdronesmall.yml")];

        Assert.Multiple(() =>
        {
            foreach (var map in maps)
            {
                checkCount++;
                var rootedPath = map.ToRootedPath();

                // Ignore anything in the test maps folder
                if (SkipTestMaps && rootedPath.ToString().StartsWith(TestMapsPath, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!resourceManager.TryContentFileRead(rootedPath, out var fileStream))
                {
                    Assert.Fail($"Map not found: {rootedPath}");
                }

                using var reader = new StreamReader(fileStream);
                var yamlStream = new YamlStream();

                yamlStream.Load(reader);

                var root = (YamlMappingNode)yamlStream.Documents[0].RootNode;
                var meta = root["meta"];
                var version = meta["format"].AsInt();

                //Yaml spaghetti to get the right components, I swear there's jquery for yaml but I couldn't be fucked finding it
                foreach (var item in ((YamlSequenceNode)root.Children["entities"]).Cast<YamlMappingNode>())
                {
                    foreach (YamlMappingNode subEnt in (YamlSequenceNode)item.Children["entities"])
                    {
                        var comps = (YamlSequenceNode)subEnt.Children["components"];

                        foreach (var atmosComp in comps.Children.Cast<YamlMappingNode>())
                        {
                            if (atmosComp.Children["type"].ToString() != "GridAtmosphere")
                                continue;

                            var dataPoints = (YamlMappingNode)atmosComp.Children["data"];
                            if (!atmosComp.Children.Keys.Contains("uniqueMixes"))
                                continue;
                            var mixes = (YamlSequenceNode)dataPoints.Children["uniqueMixes"];
                            foreach (YamlMappingNode mix in mixes.Children.Cast<YamlMappingNode>())
                            {
                                var moles = (YamlSequenceNode)mix.Children["moles"];
                                Assert.That(moles.Count(), Is.EqualTo(Atmospherics.AdjustedNumberOfGases), $"Invalid Mole count in map: {rootedPath}");
                            }
                        }
                    }
                }
            }
        });

        await pair.CleanReturnAsync();
        Console.WriteLine($"Checked {checkCount} shuttles!");
    }
}
