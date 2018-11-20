using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ICSharpCode.SharpZipLib.Zip;
using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Results);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly string Configuration = IsLocalBuild ? "Debug" : "Release";

    [Solution("LibHac.sln")] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath TempDirectory => RootDirectory / ".tmp";
    AbsolutePath CliCoreDir => TempDirectory / "hactoolnet_netcoreapp2.1";
    AbsolutePath CliFrameworkDir => TempDirectory / "hactoolnet_net46";
    AbsolutePath CliFrameworkZip => ArtifactsDirectory / "hactoolnet.zip";
    AbsolutePath CliCoreZip => ArtifactsDirectory / "hactoolnet_netcore.zip";


    Project LibHacProject => Solution.GetProject("LibHac").NotNull();
    Project HactoolnetProject => Solution.GetProject("hactoolnet").NotNull();

    Target Clean => _ => _
        .Executes(() =>
        {
            DeleteDirectories(GlobDirectories(SourceDirectory, "**/bin", "**/obj"));
            DeleteDirectories(GlobDirectories(TestsDirectory, "**/bin", "**/obj"));
            EnsureCleanDirectory(ArtifactsDirectory);
            EnsureCleanDirectory(CliCoreDir);
            EnsureCleanDirectory(CliFrameworkDir);
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .EnableNoRestore()
                .SetConfiguration(Configuration));

            var publishSettings = new DotNetPublishSettings()
                .EnableNoRestore()
                .SetConfiguration(Configuration);

            DotNetPublish(s => publishSettings
                .SetProject(HactoolnetProject)
                .SetFramework("netcoreapp2.1")
                .SetOutput(CliCoreDir));

            DotNetPublish(s => publishSettings
                .SetProject(HactoolnetProject)
                .SetFramework("net46")
                .SetOutput(CliFrameworkDir));
        });

    Target Pack => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetPack(s => s
                .SetProject(LibHacProject)
                .EnableNoBuild()
                .SetConfiguration(Configuration)
                .EnableIncludeSymbols()
                .SetOutputDirectory(ArtifactsDirectory));
        });

    Target Zip => _ => _
        .DependsOn(Pack)
        .Executes(() =>
        {
            string[] namesFx = Directory.EnumerateFiles(CliFrameworkDir, "*.exe")
                .Concat(Directory.EnumerateFiles(CliFrameworkDir, "*.dll"))
                .ToArray();

            string[] namesCore = Directory.EnumerateFiles(CliCoreDir, "*.json")
                .Concat(Directory.EnumerateFiles(CliCoreDir, "*.dll"))
                .ToArray();

            ZipFiles(CliFrameworkZip, namesFx);
            ZipFiles(CliCoreZip, namesCore);

            if (Host == HostType.AppVeyor)
            {
                PushArtifact(CliFrameworkZip);
                PushArtifact(CliCoreZip);
            }
        });

    Target Results => _ => _
        .DependsOn(Zip)
        .Executes(() =>
        {
            Console.WriteLine("SHA-1:");
            using (SHA1 sha = SHA1.Create())
            {
                foreach (string filename in Directory.EnumerateFiles(ArtifactsDirectory))
                {
                    using (var stream = new FileStream(filename, FileMode.Open))
                    {
                        string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                        Console.WriteLine($"{hash} - {Path.GetFileName(filename)}");
                    }
                }

                System.Console.WriteLine();
                foreach (string filename in Directory.EnumerateFiles(CliCoreDir))
                {
                    using (var stream = new FileStream(filename, FileMode.Open))
                    {
                        string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                        Console.WriteLine($"{hash} - {Path.GetFileName(filename)}");
                    }
                }

                System.Console.WriteLine();
                foreach (string filename in Directory.EnumerateFiles(CliFrameworkDir))
                {
                    using (var stream = new FileStream(filename, FileMode.Open))
                    {
                        string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                        Console.WriteLine($"{hash} - {Path.GetFileName(filename)}");
                    }
                }
            }
        });

    public static void ZipFiles(string outFile, string[] files)
    {
        using (var s = new ZipOutputStream(File.Create(outFile)))
        {
            s.SetLevel(9);

            foreach (string file in files)
            {
                var entry = new ZipEntry(Path.GetFileName(file));
                entry.DateTime = DateTime.UnixEpoch;

                using (FileStream fs = File.OpenRead(file))
                {
                    entry.Size = fs.Length;
                    s.PutNextEntry(entry);
                    fs.CopyTo(s);
                }
            }
        }
    }

    public static void PushArtifact(string path)
    {
        if (!File.Exists(path))
        {

            Console.WriteLine(path);
        }

        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = "appveyor";
        psi.Arguments = $"PushArtifact \"{path}\"";
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        Process proc = new Process
        {
            StartInfo = psi
        };

        proc.Start();

        proc.WaitForExit();
    }
}
