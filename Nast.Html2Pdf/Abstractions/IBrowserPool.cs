namespace Nast.Html2Pdf.Abstractions
{
    /// <summary>
    /// Interface for browser pool
    /// </summary>
    public interface IBrowserPool : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Gets a page from the pool
        /// </summary>
        Task<IPooledPage> GetPageAsync();

        /// <summary>
        /// Returns a page to the pool
        /// </summary>
        Task ReturnPageAsync(IPooledPage page);
    }
}
