using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Wxsg.Tests;

public class WpfSampleRegressionTests
{
    [Fact]
    public void MultiBinding_ItemsSource_Sample_Builds_And_Uses_DependencyProperty_Binding()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var artifact = BuildSample(
            "samples/multibinding/MultiBindingSample.csproj",
            "wpf-sample-multibinding-itemsource");

        var generatedCode = artifact.ReadGeneratedCSharp();

        Assert.Contains("BindingOperations.SetBinding(", generatedCode, StringComparison.Ordinal);
        Assert.Contains("ItemsControl.ItemsSourceProperty", generatedCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".ItemsSource.Add(", generatedCode, StringComparison.Ordinal);
        Assert.DoesNotContain("IEnumerable.Add(", generatedCode, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiBinding_DependencyProperty_Sample_Builds_And_Uses_DependencyProperty_Binding()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var artifact = BuildSample(
            "samples/multibinding-properties/MultiBindingPropertySample.csproj",
            "wpf-sample-multibinding-properties");

        var generatedCode = artifact.ReadGeneratedCSharp();

        Assert.Contains("BindingOperations.SetBinding(", generatedCode, StringComparison.Ordinal);
        Assert.Contains("TextBlock.TextProperty", generatedCode, StringComparison.Ordinal);
        Assert.Contains("UIElement.VisibilityProperty", generatedCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Visibility = __node", generatedCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Text = __node", generatedCode, StringComparison.Ordinal);
    }

    [Fact]
    public void MergedDictionaries_Sample_Builds_Successfully()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var artifact = BuildSample(
            "samples/mergeddictionaries/MergedDictionariesSample.csproj",
            "wpf-sample-mergeddictionaries");

        var generatedCode = artifact.ReadGeneratedCSharp();

        Assert.Contains("MergedDictionaries.Add(", generatedCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ResourceDictionary_XArray_Binding_Sample_Assigns_Keyed_Array_Resources()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var artifact = BuildSample(
            "samples/resourcearraybinding/ResourceArrayBindingSample.csproj",
            "wpf-sample-resource-array-binding");

        var generatedCode = artifact.ReadGeneratedCSharp();

        Assert.Contains("__root.Resources = __node0;", generatedCode, StringComparison.Ordinal);
        Assert.Contains("__node0.Add(\"toolBoxItems\", __node1);", generatedCode, StringComparison.Ordinal);
        Assert.Contains(
            "Source = __WXSG_ResolveStaticResource(__node",
            generatedCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "__root.Resources.Add(\"System.Windows.ResourceDictionary\"",
            generatedCode,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WpfEmitter_Treats_XNull_As_Clr_Null_For_BrushProperties()
    {
        var repositoryRoot = GetWxsgRepositoryRoot();
        var generatorProject = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.WPF",
            "XamlToCSharpGenerator.WPF.csproj");

        var buildOutput = RunProcess(
            repositoryRoot,
            "dotnet",
            "build \"" + generatorProject + "\" -c Debug --no-restore --nologo -f netstandard2.0");
        Assert.True(buildOutput.ExitCode == 0, buildOutput.Output);

        var generatorAssemblyPath = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.WPF",
            "bin",
            "Debug",
            "netstandard2.0",
            "XamlToCSharpGenerator.WPF.dll");

        var generatorAssembly = Assembly.LoadFrom(generatorAssemblyPath);
        var emitterType = generatorAssembly.GetType(
            "XamlToCSharpGenerator.WPF.Emission.WpfCodeEmitter",
            throwOnError: true);

        var method = emitterType!.GetMethod(
            "ConvertLiteralExpression",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var converted = method!.Invoke(
            null,
            new object[] { "\"{x:Null}\"", "System.Windows.Media.Brush", null! });

        Assert.Equal("null", converted);
    }

    [Fact]
    public void WpfEmitter_Resolves_StaticResource_In_Binding_Source_Arguments()
    {
        var generatorAssembly = BuildAndLoadWpfEmitterAssembly();
        var graphEmitterType = generatorAssembly.GetType(
            "XamlToCSharpGenerator.WPF.Emission.WpfCodeEmitter+GraphEmitter",
            throwOnError: true);

        var method = graphEmitterType!.GetMethod(
            "BuildBindingMarkupArgumentExpression",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var converted = method!.Invoke(
            null,
            new object[] { "{StaticResource toolBoxItems}", "object", "this" }) as string;

        Assert.Equal("__WXSG_ResolveStaticResource(this, \"{StaticResource toolBoxItems}\")", converted);
    }

    [Fact]
    public void WpfEmitter_Resolves_XType_In_RelativeSource_AncestorType_Arguments()
    {
        var generatorAssembly = BuildAndLoadWpfEmitterAssembly();
        var graphEmitterType = generatorAssembly.GetType(
            "XamlToCSharpGenerator.WPF.Emission.WpfCodeEmitter+GraphEmitter",
            throwOnError: true);

        var method = graphEmitterType!.GetMethod(
            "TryBuildRelativeSourceExpression",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var arguments = new object[] { "{RelativeSource FindAncestor, AncestorType={x:Type Button}, AncestorLevel=2}", string.Empty };
        var success = (bool)method!.Invoke(null, arguments)!;

        Assert.True(success);
        Assert.Equal(
            "new global::System.Windows.Data.RelativeSource(global::System.Windows.Data.RelativeSourceMode.FindAncestor, __WXSG_ResolveTypeToken(\"Button\"), 2)",
            arguments[1]);
    }

    private static Assembly BuildAndLoadWpfEmitterAssembly()
    {
        var repositoryRoot = GetWxsgRepositoryRoot();
        var generatorProject = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.WPF",
            "XamlToCSharpGenerator.WPF.csproj");

        var buildOutput = RunProcess(
            repositoryRoot,
            "dotnet",
            "build \"" + generatorProject + "\" -c Debug --no-restore --nologo -f netstandard2.0");
        Assert.True(buildOutput.ExitCode == 0, buildOutput.Output);

        var generatorAssemblyPath = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.WPF",
            "bin",
            "Debug",
            "netstandard2.0",
            "XamlToCSharpGenerator.WPF.dll");

        return Assembly.LoadFrom(generatorAssemblyPath);
    }

    private static SampleBuildArtifact BuildSample(string relativeProjectPath, string scenario)
    {
        var repositoryRoot = GetWxsgRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, relativeProjectPath.Replace('/', Path.DirectorySeparatorChar));
        var workspaceDirectory = CreateTemporaryDirectory(repositoryRoot, scenario);
        var generatedDirectory = Path.Combine(workspaceDirectory, "generated");

        try
        {
            Directory.CreateDirectory(generatedDirectory);

            var restoreOutput = RunProcess(
                repositoryRoot,
                "dotnet",
                RestoreArguments(projectPath));
            Assert.True(restoreOutput.ExitCode == 0, restoreOutput.Output);

            var buildOutput = RunProcess(
                repositoryRoot,
                "dotnet",
                BuildArguments(projectPath, generatedDirectory));

            Assert.True(buildOutput.ExitCode == 0, buildOutput.Output);
            return new SampleBuildArtifact(workspaceDirectory, generatedDirectory);
        }
        catch
        {
            TryDeleteDirectory(workspaceDirectory);
            throw;
        }
    }

    private static string BuildArguments(string projectPath, string generatedDirectory)
    {
        return new StringBuilder()
            .Append("build \"")
            .Append(projectPath)
            .Append("\" --no-restore -t:Rebuild --nologo -c Debug -m:1 /nodeReuse:false --disable-build-servers")
            .Append(BuildPropertyArguments(generatedDirectory))
            .ToString();
    }

    private static string RestoreArguments(string projectPath)
    {
        return new StringBuilder()
            .Append("restore \"")
            .Append(projectPath)
            .Append("\" --nologo -m:1 /nodeReuse:false --disable-build-servers")
            .ToString();
    }

    private static string BuildPropertyArguments(string generatedDirectory)
    {
        return new StringBuilder()
            .Append(" -p:EmitCompilerGeneratedFiles=true")
            .Append(" -p:CompilerGeneratedFilesOutputPath=\"")
            .Append(NormalizeForMsBuild(generatedDirectory))
            .Append('"')
            .ToString();
    }

    private static (int ExitCode, string Output) RunProcess(string workingDirectory, string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        System.Threading.Tasks.Task.WaitAll(stdoutTask, stderrTask);

        var outputBuilder = new StringBuilder();
        outputBuilder.Append(stdoutTask.Result);
        outputBuilder.Append(stderrTask.Result);
        return (process.ExitCode, outputBuilder.ToString());
    }

    private static string GetWxsgRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }

    private static string NormalizeForMsBuild(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string CreateTemporaryDirectory(string repositoryRoot, string scenario)
    {
        var directory = Path.Combine(
            repositoryRoot,
            ".tmp-tests",
            scenario,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for test workspaces.
        }
    }

    private sealed class SampleBuildArtifact : IDisposable
    {
        public SampleBuildArtifact(string workspaceDirectory, string generatedDirectory)
        {
            WorkspaceDirectory = workspaceDirectory;
            GeneratedDirectory = generatedDirectory;
        }

        public string WorkspaceDirectory { get; }

        public string GeneratedDirectory { get; }

        public string ReadGeneratedCSharp()
        {
            var generatedFiles = Directory.GetFiles(GeneratedDirectory, "*.cs", SearchOption.AllDirectories);
            Assert.NotEmpty(generatedFiles);

            var builder = new StringBuilder();
            foreach (var file in generatedFiles.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine(File.ReadAllText(file));
            }

            return builder.ToString();
        }

        public void Dispose()
        {
            TryDeleteDirectory(WorkspaceDirectory);
        }
    }
}
