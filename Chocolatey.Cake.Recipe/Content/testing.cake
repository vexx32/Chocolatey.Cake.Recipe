using System.Xml.Linq;
using System.Xml.XPath;

///////////////////////////////////////////////////////////////////////////////
// TASK DEFINITIONS
///////////////////////////////////////////////////////////////////////////////

BuildParameters.Tasks.InstallOpenCoverTask = Task("Install-OpenCover")
    .WithCriteria(() => BuildParameters.BuildAgentOperatingSystem == PlatformFamily.Windows, "Not running on windows")
    .Does(() => RequireTool(ToolSettings.OpenCoverTool, () => {
    }));

BuildParameters.Tasks.TestNUnitTask = Task("Test-NUnit")
    .IsDependentOn("Install-OpenCover")
    .WithCriteria(() => BuildParameters.Configuration != "ReleaseOfficial", "Skipping since build configuration is ReleaseOfficial")
    .WithCriteria(() => DirectoryExists(BuildParameters.Paths.Directories.PublishedNUnitTests), "No published NUnit tests")
    .Does(() => RequireTool(ToolSettings.NUnitTool, () => {
        EnsureDirectoryExists(BuildParameters.Paths.Directories.NUnitTestResults);

        if (BuildParameters.BuildAgentOperatingSystem == PlatformFamily.Windows)
        {
            OpenCover(tool => {
                tool.NUnit3(GetFiles(BuildParameters.Paths.Directories.PublishedNUnitTests + (BuildParameters.TestFilePattern ?? "/**/*Tests.dll")), new NUnit3Settings {
                    NoResults = true
                });
            },
            BuildParameters.Paths.Files.TestCoverageOutputFilePath,
            new OpenCoverSettings
            {
                OldStyle = true,
                ReturnTargetCodeOffset = 0
            }
                .WithFilter(ToolSettings.TestCoverageFilter)
                .ExcludeByAttribute(ToolSettings.TestCoverageExcludeByAttribute)
                .ExcludeByFile(ToolSettings.TestCoverageExcludeByFile));
        }
    })
);

BuildParameters.Tasks.TestxUnitTask = Task("Test-xUnit")
    .IsDependentOn("Install-OpenCover")
    .WithCriteria(() => BuildParameters.Configuration != "ReleaseOfficial", "Skipping since build configuration is ReleaseOfficial")
    .WithCriteria(() => DirectoryExists(BuildParameters.Paths.Directories.PublishedxUnitTests), "No published xUnit tests")
    .Does(() => RequireTool(ToolSettings.XUnitTool, () => {
    EnsureDirectoryExists(BuildParameters.Paths.Directories.xUnitTestResults);

        if (BuildParameters.BuildAgentOperatingSystem == PlatformFamily.Windows)
        {
            OpenCover(tool => {
                tool.XUnit2(GetFiles(BuildParameters.Paths.Directories.PublishedxUnitTests + (BuildParameters.TestFilePattern ?? "/**/*Tests.dll")), new XUnit2Settings {
                    OutputDirectory = BuildParameters.Paths.Directories.xUnitTestResults,
                    XmlReport = true,
                    NoAppDomain = true
                });
            },
            BuildParameters.Paths.Files.TestCoverageOutputFilePath,
            new OpenCoverSettings
            {
                OldStyle = true,
                ReturnTargetCodeOffset = 0
            }
                .WithFilter(ToolSettings.TestCoverageFilter)
                .ExcludeByAttribute(ToolSettings.TestCoverageExcludeByAttribute)
                .ExcludeByFile(ToolSettings.TestCoverageExcludeByFile));
        }
    })
);

BuildParameters.Tasks.DotNetCoreTestTask = Task("DotNetCoreTest")
    .IsDependentOn("Install-OpenCover")
    .Does(() => {

    var msBuildSettings = new DotNetCoreMSBuildSettings()
                            .WithProperty("Version", BuildParameters.Version.SemVersion)
                            .WithProperty("AssemblyVersion", BuildParameters.Version.Version)
                            .WithProperty("FileVersion",  BuildParameters.Version.Version)
                            .WithProperty("AssemblyInformationalVersion", BuildParameters.Version.InformationalVersion);

    if (BuildParameters.BuildAgentOperatingSystem != PlatformFamily.Windows)
    {
        var frameworkPathOverride = new FilePath(typeof(object).Assembly.Location).GetDirectory().FullPath + "/";

        // Use FrameworkPathOverride when not running on Windows.
        Information("Restore will use FrameworkPathOverride={0} since not building on Windows.", frameworkPathOverride);
        msBuildSettings.WithProperty("FrameworkPathOverride", frameworkPathOverride);
    }

    var projects = GetFiles(BuildParameters.TestDirectoryPath + (BuildParameters.TestFilePattern ?? "/**/*Tests.csproj"));
    // We create the coverlet settings here so we don't have to create the filters several times
    var coverletSettings = new CoverletSettings
    {
        CollectCoverage         = true,
        // It is problematic to merge the reports into one, as such we use a custom directory for coverage results
        CoverletOutputDirectory = BuildParameters.Paths.Directories.TestCoverage.Combine("coverlet"),
        CoverletOutputFormat    = CoverletOutputFormat.opencover,
        ExcludeByFile           = ToolSettings.TestCoverageExcludeByFile.Split(new [] {';' }, StringSplitOptions.None).ToList(),
        ExcludeByAttribute      = ToolSettings.TestCoverageExcludeByAttribute.Split(new [] {';' }, StringSplitOptions.None).ToList()
    };

    foreach (var filter in ToolSettings.TestCoverageFilter.Split(new [] {' ' }, StringSplitOptions.None))
    {
        if (filter[0] == '+')
        {
            coverletSettings.WithInclusion(filter.TrimStart('+'));
        }
        else if (filter[0] == '-')
        {
            coverletSettings.WithFilter(filter.TrimStart('-'));
        }
    }
    var settings = new DotNetCoreTestSettings
    {
        Configuration = BuildParameters.Configuration,
        NoBuild = true
    };

    foreach (var project in projects)
    {
        Action<ICakeContext> testAction = tool =>
        {
            tool.DotNetCoreTest(project.FullPath, settings);
        };

        var parsedProject = ParseProject(project, BuildParameters.Configuration);

        var coverletPackage = parsedProject.GetPackage("coverlet.msbuild");
        bool shouldAddSourceLinkArgument = false; // Set it to false by default due to OpenCover
        if (coverletPackage != null)
        {
            // If the version is a pre-release, we will assume that it is a later
            // version than what we need, and thus TryParse will return false.
            // If TryParse is successful we need to compare the coverlet version
            // to ensure it is higher or equal to the version that includes the fix
            // for using the SourceLink argument.
            // https://github.com/coverlet-coverage/coverlet/issues/882
            Version coverletVersion;
            shouldAddSourceLinkArgument = !Version.TryParse(coverletPackage.Version, out coverletVersion)
                || coverletVersion >= Version.Parse("2.9.1");
        }

        settings.ArgumentCustomization = args => {
            args.AppendMSBuildSettings(msBuildSettings, Context.Environment);
            if (shouldAddSourceLinkArgument && parsedProject.HasPackage("Microsoft.SourceLink.GitHub"))
            {
                args.Append("/p:UseSourceLink=true");
            }
            return args;
        };

        if (parsedProject.IsNetCore && coverletPackage != null)
        {
            coverletSettings.CoverletOutputName = parsedProject.RootNameSpace.Replace('.', '-');
            DotNetCoreTest(project.FullPath, settings, coverletSettings);
        }
        else if (BuildParameters.BuildAgentOperatingSystem != PlatformFamily.Windows)
        {
            testAction(Context);
        }
        else
        {
            if (BuildParameters.BuildAgentOperatingSystem == PlatformFamily.Windows)
            {
                // We can not use msbuild properties together with opencover
                settings.ArgumentCustomization = null;
                OpenCover(testAction,
                    BuildParameters.Paths.Files.TestCoverageOutputFilePath,
                    new OpenCoverSettings {
                        ReturnTargetCodeOffset = 0,
                        OldStyle = true,
                        Register = "user",
                        MergeOutput = FileExists(BuildParameters.Paths.Files.TestCoverageOutputFilePath)
                    }
                    .WithFilter(ToolSettings.TestCoverageFilter)
                    .ExcludeByAttribute(ToolSettings.TestCoverageExcludeByAttribute)
                    .ExcludeByFile(ToolSettings.TestCoverageExcludeByFile));
            }
        }
    }
});

BuildParameters.Tasks.GenerateFriendlyTestReportTask = Task("Generate-FriendlyTestReport")
    .IsDependentOn("Test-NUnit")
    .IsDependentOn("Test-xUnit")
    .WithCriteria(() => BuildParameters.BuildAgentOperatingSystem == PlatformFamily.Windows, "Skipping due to not running on Windows")
    .Does(() => RequireTool(ToolSettings.ReportUnitTool, () =>
    {
        var possibleDirectories = new[] {
            BuildParameters.Paths.Directories.xUnitTestResults,
            BuildParameters.Paths.Directories.NUnitTestResults,
        };

        foreach (var directory in possibleDirectories.Where((d) => DirectoryExists(d)))
        {
            ReportUnit(directory, directory, new ReportUnitSettings());
        }
    })
);

BuildParameters.Tasks.ReportCodeCoverageMetricsTask = Task("Report-Code-Coverage-Metrics")
    .WithCriteria(() => BuildSystem.IsRunningOnTeamCity, "Skipping due to not running on TeamCity")
    .Does(() => {
        var coverageFiles = GetFiles(BuildParameters.Paths.Directories.TestCoverage + "/coverlet/*.xml");
        if (FileExists(BuildParameters.Paths.Files.TestCoverageOutputFilePath))
        {
            coverageFiles += BuildParameters.Paths.Files.TestCoverageOutputFilePath;
        }

        double totalVisitedClasses = 0.0;
        double totalClasses = 0.0;
        double totalVisitedMethods = 0.0;
        double totalMethods = 0.0;
        double totalVisitedSequencePoints = 0.0;
        double totalSequencePoints = 0.0;
        double totalVisitedBranchPoints = 0.0;
        double totalBranchPoints = 0.0;

        foreach(var coverageFile in coverageFiles)
        {
            BuildParameters.BuildProvider.UploadArtifact(coverageFile);

            XDocument doc = XDocument.Load(coverageFile.FullPath);
            XElement summary = doc.XPathSelectElement("/CoverageSession/Summary");

            totalVisitedClasses += Convert.ToDouble(summary.Attribute("visitedClasses").Value);
            totalClasses += Convert.ToDouble(summary.Attribute("numClasses").Value);
            totalVisitedMethods += Convert.ToDouble(summary.Attribute("visitedMethods").Value);
            totalMethods += Convert.ToDouble(summary.Attribute("numMethods").Value);
            totalVisitedSequencePoints += Convert.ToDouble(summary.Attribute("visitedSequencePoints").Value);
            totalSequencePoints += Convert.ToDouble(summary.Attribute("numSequencePoints").Value);
            totalVisitedBranchPoints += Convert.ToDouble(summary.Attribute("visitedBranchPoints").Value);
            totalBranchPoints += Convert.ToDouble(summary.Attribute("numBranchPoints").Value);
        }

        // Classes.
        ReportCoverageMetric(
            totalVisitedClasses,
            totalClasses,
            "CodeCoverageAbsCCovered",
            "CodeCoverageAbsCTotal",
            "CodeCoverageC");

        // Methods.
        ReportCoverageMetric(
            totalVisitedMethods,
            totalMethods,
            "CodeCoverageAbsMCovered",
            "CodeCoverageAbsMTotal",
            "CodeCoverageM");

        // Sequence points / statements.
        ReportCoverageMetric(
            totalVisitedSequencePoints,
            totalSequencePoints,
            "CodeCoverageAbsSCovered",
            "CodeCoverageAbsSTotal",
            "CodeCoverageS");

        // Branches.
        ReportCoverageMetric(
            totalVisitedBranchPoints,
            totalBranchPoints,
            "CodeCoverageAbsBCovered",
            "CodeCoverageAbsBTotal",
            "CodeCoverageB");
});

private void ReportCoverageMetric(
    double totalVisited,
    double total,
    string tcVisitedKey,
    string tcTotalKey,
    string tcCoverageKey)
{
    double coverage = (totalVisited / total) * 100;

    Information($"##teamcity[buildStatisticValue key='{tcVisitedKey}' value='{totalVisited}']");
    Information($"##teamcity[buildStatisticValue key='{tcTotalKey}' value='{total}']");
    Information($"##teamcity[buildStatisticValue key='{tcCoverageKey}' value='{coverage}']");
}

BuildParameters.Tasks.GenerateLocalCoverageReportTask = Task("Generate-LocalCoverageReport")
    .WithCriteria(() => BuildParameters.IsLocalBuild, "Skipping due to not running a local build")
    .Does(() => RequireTool(BuildParameters.IsDotNetCoreBuild ? ToolSettings.ReportGeneratorGlobalTool : ToolSettings.ReportGeneratorTool, () => {
        var coverageFiles = GetFiles(BuildParameters.Paths.Directories.TestCoverage + "/coverlet/*.xml");
        if (FileExists(BuildParameters.Paths.Files.TestCoverageOutputFilePath))
        {
            coverageFiles += BuildParameters.Paths.Files.TestCoverageOutputFilePath;
        }

        if (coverageFiles.Any())
        {
            var settings = new ReportGeneratorSettings();
            if (BuildParameters.IsDotNetCoreBuild && BuildParameters.BuildAgentOperatingSystem != PlatformFamily.Windows)
            {
                // Workaround until 0.38.5+ version of cake is released
                // https://github.com/cake-build/cake/pull/2824
                settings.ToolPath = Context.Tools.Resolve("reportgenerator");
            }

            ReportGenerator(coverageFiles, BuildParameters.Paths.Directories.TestCoverage, settings);
        }
        else
        {
            Warning("No coverage files was found, no local report is generated!");
        }
    })
);

BuildParameters.Tasks.TestTask = Task("Test");