namespace Jacere.Crawler.Core
{
    public interface IDataStorageCommand
    {
        string DatabasePath { get; }
        int DelayInterval { get; }
    }
}
