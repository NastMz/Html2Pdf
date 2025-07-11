namespace Nast.Html2Pdf.Abstractions
{
    /// <summary>
    /// Interface for a pooled page
    /// </summary>
    public interface IPooledPage : IDisposable
    {
        /// <summary>
        /// Playwright page
        /// </summary>
        Microsoft.Playwright.IPage Page { get; }

        /// <summary>
        /// Indicates if the page is in use
        /// </summary>
        bool IsInUse { get; }

        /// <summary>
        /// Marks the page as in use
        /// </summary>
        void MarkAsInUse();

        /// <summary>
        /// Marks the page as available
        /// </summary>
        void MarkAsAvailable();
    }
}
