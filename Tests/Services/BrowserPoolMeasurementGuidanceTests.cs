using Nast.Html2Pdf.Services;

namespace Nast.Html2Pdf.Tests.Services
{
    public class BrowserPoolMeasurementGuidanceTests
    {
        [Fact]
        public void Describe_ShouldPrioritizeConsumerVisibleRuntimeOperations()
        {
            var operations = new[]
            {
                BrowserPoolOperation.AcquirePage,
                BrowserPoolOperation.BrowserProvisioning,
                BrowserPoolOperation.BrowserStartup,
                BrowserPoolOperation.PageCreation
            };

            foreach (var operation in operations)
            {
                var guidance = BrowserPoolMeasurementGuidance.Describe(operation);

                guidance.Focus.ShouldBe(BrowserPoolOptimizationFocus.ConsumerRuntime);
                guidance.AffectsLibraryConsumers.ShouldBeTrue();
                guidance.Rationale.ShouldContain("consumer");
            }
        }

        [Fact]
        public void Describe_ShouldTreatCleanupAndHostNoiseAsNonConsumerRuntimeEvenWithinBrowserInterop()
        {
            var startup = BrowserPoolMeasurementGuidance.Describe(BrowserPoolOperation.BrowserStartup);
            var closeBrowser = BrowserPoolMeasurementGuidance.Describe(BrowserPoolOperation.CloseBrowser);
            var closePage = BrowserPoolMeasurementGuidance.Describe(BrowserPoolOperation.ClosePage);
            var disposePool = BrowserPoolMeasurementGuidance.Describe(BrowserPoolOperation.DisposePool);
            var returnPage = BrowserPoolMeasurementGuidance.Describe(BrowserPoolOperation.ReturnPage);

            startup.Focus.ShouldBe(BrowserPoolOptimizationFocus.ConsumerRuntime);
            closeBrowser.Focus.ShouldBe(BrowserPoolOptimizationFocus.TestOnlyOverheadOrBrowserHostNoise);
            closePage.Focus.ShouldBe(BrowserPoolOptimizationFocus.TestOnlyOverheadOrBrowserHostNoise);
            disposePool.Focus.ShouldBe(BrowserPoolOptimizationFocus.TestOnlyOverheadOrBrowserHostNoise);
            returnPage.Focus.ShouldBe(BrowserPoolOptimizationFocus.TestOnlyOverheadOrBrowserHostNoise);

            closeBrowser.AffectsLibraryConsumers.ShouldBeFalse();
            closePage.AffectsLibraryConsumers.ShouldBeFalse();
            disposePool.AffectsLibraryConsumers.ShouldBeFalse();
            returnPage.AffectsLibraryConsumers.ShouldBeFalse();
            closeBrowser.Rationale.ShouldContain("noise");
        }
    }
}
