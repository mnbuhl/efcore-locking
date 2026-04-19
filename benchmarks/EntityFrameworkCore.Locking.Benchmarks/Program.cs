using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

if (args.FirstOrDefault(a => a.StartsWith("--version=")) is not { } versionArg)
{
    Console.Error.WriteLine("Usage: --version=<x.y.z> [BenchmarkDotNet args...]");
    return 1;
}

var version = versionArg["--version=".Length..];
var bdnArgs = args.Where(a => !a.StartsWith("--version=")).ToArray();

var config = DefaultConfig.Instance.WithArtifactsPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "results", $"v{version}")
);

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(bdnArgs, config);
return 0;
