using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.IO;
using Content.IntegrationTests;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.UnitTesting;
using NUnit.Framework.Api;

namespace Content.YAMLLinter
{
    internal static class Program
    {
        private static readonly List<string> LogBuffer = new();
        private static string ResultsPath => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "yaml_lint_results.txt");

        private static void Out(string message)
        {
            Console.WriteLine(message);
            LogBuffer.Add(message);
        }

        private static void FlushLog()
        {
            try
            {
                var path = ResultsPath;
                var full = Path.GetFullPath(path);
                File.WriteAllLines(full, LogBuffer);
            }
            catch
            {
                // best-effort; ignore IO errors
            }
        }

        private static async Task<int> Main(string[] _)
        {
            PoolManager.Startup();
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            // HL: Manually build the test layout because this testing system is a bit jank
            var builder = new DefaultTestAssemblyBuilder();
            var settings = new Dictionary<string, object>();
            builder.Build(Assembly.GetExecutingAssembly(), settings);

            var (errors, fieldErrors) = await RunValidation();

            var count = errors.Count + fieldErrors.Count;

            if (count == 0)
            {
                Out($"No errors found in {(int)stopwatch.Elapsed.TotalMilliseconds} ms.");
                PoolManager.Shutdown();
                FlushLog();
                return 0;
            }

            foreach (var (file, errorHashset) in errors)
            {
                foreach (var errorNode in errorHashset)
                {
                    Out($"::error file={file},line={errorNode.Node.Start.Line},col={errorNode.Node.Start.Column}::{file}({errorNode.Node.Start.Line},{errorNode.Node.Start.Column})  {errorNode.ErrorReason}");
                }
            }

            foreach (var error in fieldErrors)
            {
                Out(error);
            }

            Out($"{count} errors found in {(int)stopwatch.Elapsed.TotalMilliseconds} ms.");
            PoolManager.Shutdown();
            FlushLog();
            return -1;
        }

        private static async Task<(Dictionary<string, HashSet<ErrorNode>> YamlErrors, List<string> FieldErrors, Assembly[] Assemblies)>
            ValidateClient()
        {
            await using var pair = await PoolManager.GetServerClient();
            var result = await ValidateInstance(pair.Client);
            await pair.CleanReturnAsync();
            return result;
        }

        private static async Task<(Dictionary<string, HashSet<ErrorNode>> YamlErrors, List<string> FieldErrors, Assembly[] Assemblies)>
            ValidateServer()
        {
            await using var pair = await PoolManager.GetServerClient();
            var result = await ValidateInstance(pair.Server);
            await pair.CleanReturnAsync();
            return result;
        }

        private static async Task<(Dictionary<string, HashSet<ErrorNode>>, List<string>, Assembly[])> ValidateInstance(
            RobustIntegrationTest.IntegrationInstance instance)
        {
            var protoMan = instance.ResolveDependency<IPrototypeManager>();
            var refl = instance.ResolveDependency<IReflectionManager>();
            Dictionary<string, HashSet<ErrorNode>> yamlErrors = default!;
            List<string> fieldErrors = default!;
            Assembly[] assemblies = default!;

            await instance.WaitPost(() =>
            {
                var engineErrors = protoMan.ValidateDirectory(new ResPath("/EnginePrototypes"), out var engPrototypes);
                yamlErrors = protoMan.ValidateDirectory(new ResPath("/Prototypes"), out var prototypes);

                // Merge engine & content prototypes
                foreach (var (kind, instances) in engPrototypes)
                {
                    if (prototypes.TryGetValue(kind, out var existing))
                        existing.UnionWith(instances);
                    else
                        prototypes[kind] = instances;
                }

                foreach (var (kind, set) in engineErrors)
                {
                    if (yamlErrors.TryGetValue(kind, out var existing))
                        existing.UnionWith(set);
                    else
                        yamlErrors[kind] = set;
                }

                fieldErrors = protoMan.ValidateStaticFields(prototypes);
                assemblies = refl.Assemblies.ToArray();
            });

            return (yamlErrors, fieldErrors, assemblies);
        }

        public static async Task<(Dictionary<string, HashSet<ErrorNode>> YamlErrors, List<string> FieldErrors)>
            RunValidation()
        {
            var serverTask = ValidateServer();
            var clientTask = ValidateClient();

            await Task.WhenAll(serverTask, clientTask);

            var (serverYamlErrors, serverFieldErrors, serverAssemblies) = await serverTask;
            var (clientYamlErrors, clientFieldErrors, clientAssemblies) = await clientTask;

            var serverTypes = serverAssemblies.SelectMany(n => n.GetTypes()).Select(t => t.Name).ToHashSet();
            var clientTypes = clientAssemblies.SelectMany(n => n.GetTypes()).Select(t => t.Name).ToHashSet();

            var yamlErrors = new Dictionary<string, HashSet<ErrorNode>>();

            foreach (var (key, val) in serverYamlErrors)
            {
                // Include all server errors marked as always relevant
                var newErrors = val.Where(n => n.AlwaysRelevant).ToHashSet();

                // We include sometimes-relevant errors if they exist both for the client & server
                if (clientYamlErrors.TryGetValue(key, out var clientVal))
                    newErrors.UnionWith(val.Intersect(clientVal));

                // Include any errors that relate to server-only types
                foreach (var errorNode in val)
                {
                    if (errorNode is FieldNotFoundErrorNode fieldNotFoundNode && !clientTypes.Contains(fieldNotFoundNode.FieldType.Name))
                    {
                        newErrors.Add(errorNode);
                    }
                }

                if (newErrors.Count != 0)
                    yamlErrors[key] = newErrors;
            }

            // Next add any always-relevant client errors.
            foreach (var (key, val) in clientYamlErrors)
            {
                var newErrors = val.Where(n => n.AlwaysRelevant).ToHashSet();
                if (newErrors.Count == 0)
                    continue;

                if (yamlErrors.TryGetValue(key, out var errors))
                    errors.UnionWith(val.Where(n => n.AlwaysRelevant));
                else
                    yamlErrors[key] = newErrors;

                // Include any errors that relate to client-only types
                foreach (var errorNode in val)
                {
                    if (errorNode is FieldNotFoundErrorNode fieldNotFoundNode && !serverTypes.Contains(fieldNotFoundNode.FieldType.Name))
                    {
                        newErrors.Add(errorNode);
                    }
                }
            }

            // Finally, combine the prototype ID field errors.
            var fieldErrors = serverFieldErrors
                .Concat(clientFieldErrors)
                .Distinct()
                .ToList();

            return (yamlErrors, fieldErrors);
        }
    }
}
