namespace Jacere.Crawler.Core
{
    public interface IDataStorageCommand : IDataCommand
    {
        string DatabasePath { get; }
    }
}
