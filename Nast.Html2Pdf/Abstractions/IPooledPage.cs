using PuppeteerSharp;

namespace Nast.Html2Pdf.Abstractions
{
    /// <summary>
    /// Interface for a pooled page
    /// </summary>
    public interface IPooledPage : IDisposable
    {
        /// <summary>
        /// PuppeteerSharp page
        /// </summary>
        IPage Page { get; }

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
