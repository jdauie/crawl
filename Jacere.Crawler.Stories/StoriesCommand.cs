using System.Threading.Tasks;
using Jacere.Crawler.Core;

namespace Jacere.Crawler.Stories
{
    public class StoriesCommand : ErrorHandlingConsoleComand, IDataStorageCommand
    {
        public const string DefaultDatabasePath = @"C:\tmp\crawl\stories.sqlite";
        public const int DefaultDelayInterval = 500;

        public string DatabasePath { get; set; } = DefaultDatabasePath;
        public int DelayInterval { get; set; } = DefaultDelayInterval;

        public StoriesCommand()
        {
            IsCommand("Stories", "Crawl storybird.com");

            HasRequiredOption("d|database-path=", "The physical path to the database.", x => DatabasePath = x);
            HasOption("i|delay-interval", "The delay between requests (in ms).", x => DelayInterval = int.Parse(x));
        }

        protected override void RunAction(string[] remainingArguments)
        {
            using (var context = new StoriesContext(this))
            {
                Task.Run(async () =>
                {
                    await context.Crawl();
                }).GetAwaiter().GetResult();
            }
        }
    }
}
