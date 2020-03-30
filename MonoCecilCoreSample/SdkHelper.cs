using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MonoCecilCoreSample {
    public static class SdkHelper {
        // Search dotnet paths for reference assemblies
        // Paths based on info from https://docs.microsoft.com/en-us/dotnet/core/distribution-packaging
        private const string DotNetPath = @"c:\Program Files\dotnet\";
        private const string PacksPath = @"packs\";
        private const string CoreRefsPath = DotNetPath + PacksPath + @"Microsoft.NETCore.App.Ref\";
        private const string StandardRefsPath = DotNetPath + PacksPath + @"NETStandard.Library.Ref\";

        private static string[] GetInstalledRefsVersions(string baseRefPath) {
            var dir = Directory.GetDirectories(baseRefPath);
            return Array.ConvertAll(dir, d => Path.GetRelativePath(baseRefPath, d));
        }

        public static string GetCoreAssemblyPath(string packVersion, string assemblyName) =>
            GetReferenceAssemblyPath(CoreRefsPath, packVersion, assemblyName);
        public static string GetStandardAssemblyPath(string packVersion, string assemblyName) =>
            GetReferenceAssemblyPath(StandardRefsPath, packVersion, assemblyName);

        private static string GetReferenceAssemblyPath(string baseRefPath, string coreVersion, string assemblyName) {
            var frameworkList = Path.Combine(baseRefPath, coreVersion, @"data\FrameworkList.xml");

            var xDocument = XDocument.Load(frameworkList);
            var (path, _) = xDocument.Descendants(@"File")
                .Select(x => (path: x.Attribute(@"Path"), name: x.Attribute("AssemblyName")))
                .FirstOrDefault(t => string.Equals(t.name.Value, assemblyName, StringComparison.InvariantCultureIgnoreCase));
            return path?.Value;
        }
        
        // Use dotnet tool to get sdk and runtime versions and paths
        private const string DotNetTool = "dotnet";

        public static string GetInstalledSdks() => RunDotNetTool(@"--list-sdks");
        public static string GetInstalledRuntimes() => RunDotNetTool(@"--list-runtimes");
        public static string GetCurrentSdkVersion() => RunDotNetTool(@"--version");

        public static string GetSdkPath(string sdkVersion) {
            var sdkListString = GetInstalledSdks();
            var sdkInfo = sdkListString.Split(Environment.NewLine).FirstOrDefault(x => x.StartsWith(sdkVersion));

            if (string.IsNullOrEmpty(sdkInfo))
                return null;
            
            var match = Regex.Match(sdkInfo, @"\[(?<path>.*)\]");
            if (!match.Groups.TryGetValue("path", out var group))
                return null;
            
            var sdkBasePath = @group.Value;
            return Path.Combine(sdkBasePath, sdkVersion);
        }

        public static string GetCurrentSdkPath() {
            var currentSdkVersion = GetCurrentSdkVersion();
            return string.IsNullOrEmpty(currentSdkVersion) ? null : GetSdkPath(currentSdkVersion);
        }

        private static string RunDotNetTool(string arguments) => 
            Process.Start(new ProcessStartInfo(DotNetTool, arguments) {
                    ErrorDialog = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                })
                ?.StandardOutput.ReadToEnd()
                .Trim();
    }
}