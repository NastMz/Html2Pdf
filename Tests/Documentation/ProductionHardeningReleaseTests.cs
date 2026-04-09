namespace Nast.Html2Pdf.Tests.Documentation
{
    public class ProductionHardeningReleaseTests
    {
        [Fact]
        public void PackageMetadata_ShouldDescribeNet8LibraryWithNet8PlusConsumptionSupport()
        {
            var projectFile = ReadRepoFile("Nast.Html2Pdf", "Nast.Html2Pdf.csproj");

            projectFile.ShouldContain("<TargetFramework>net8.0</TargetFramework>");
            projectFile.ShouldContain("<Version>1.0.1</Version>");
            projectFile.ShouldContain(".NET 8+ consuming applications");
        }

        [Fact]
        public void TestProject_ShouldTargetNet8AndNet9()
        {
            var testProjectFile = ReadRepoFile("Tests", "Nast.Html2Pdf.Tests.csproj");

            testProjectFile.ShouldContain("<TargetFrameworks>net8.0;net9.0</TargetFrameworks>");
        }

        [Fact]
        public void WorkflowAndTestScript_ShouldValidateDotNet8AndDotNet9WithoutPlaywrightInstallSteps()
        {
            var workflow = ReadRepoFile(".github", "workflows", "nuget-publish.yml");
            var script = ReadRepoFile("run-tests.ps1");
            var lowerScript = script.ToLowerInvariant();

            workflow.ShouldContain("8.0.x");
            workflow.ShouldContain("9.0.x");
            workflow.ShouldContain("HTML2PDF_TEST_DISABLE_SANDBOX");
            lowerScript.ShouldContain("net8.0");
            lowerScript.ShouldContain("net9.0");
            lowerScript.ShouldContain("puppeteersharp");
            lowerScript.ShouldNotContain("playwright");
        }

        [Fact]
        public void Documentation_ShouldMentionSecureDefaultsProvisioningAndNet8PlusSupport()
        {
            var readme = ReadRepoFile("README.md").ToLowerInvariant();

            readme.ShouldContain("secure defaults");
            readme.ShouldContain(".net 8+");
            readme.ShouldContain("supported entry points");
            readme.ShouldContain("browser provisioning");
            readme.ShouldContain("testing and ci");
            ReadRepoFile("QUICK_START.md").ToLowerInvariant().ShouldContain("skipbrowserdownload");
            ReadRepoFile("COMPREHENSIVE_GUIDE.md").ToLowerInvariant().ShouldContain("allowinsecurecertificates");
            ReadRepoFile("COMPREHENSIVE_GUIDE.md").ToLowerInvariant().ShouldContain("disablesandbox");
            ReadRepoFile("COMPREHENSIVE_GUIDE.md").ShouldContain("Version=\"1.0.1\"");
        }

        [Fact]
        public void ReleaseNotes_ShouldDescribeHardeningAndMigrationImpact()
        {
            var releaseNotes = ReadRepoFile("CHANGELOG.md");
            var lowerReleaseNotes = releaseNotes.ToLowerInvariant();

            lowerReleaseNotes.ShouldContain("secure defaults");
            lowerReleaseNotes.ShouldContain("migration");
            lowerReleaseNotes.ShouldContain(".net 8+");
            releaseNotes.ShouldContain("1.0.1");
        }

        [Fact]
        public void BuildPackageScript_ShouldDefaultToNextPatchVersion()
        {
            var buildPackageScript = ReadRepoFile("Nast.Html2Pdf", "build-package.ps1");

            buildPackageScript.ShouldContain("[string]$Version = \"1.0.1\"");
        }

        private static string ReadRepoFile(params string[] relativeSegments)
        {
            var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var filePath = Path.Combine(new[] { repoRoot }.Concat(relativeSegments).ToArray());
            return File.ReadAllText(filePath);
        }
    }
}
