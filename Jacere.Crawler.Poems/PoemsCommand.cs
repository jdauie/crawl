using System;
using System.Threading.Tasks;
using Jacere.Crawler.Core;

namespace Jacere.Crawler.Poems
{
    public class PoemsCommand : ErrorHandlingConsoleComand, IDataStorageCommand
    {
        public const string DefaultDatabasePath = @"C:\tmp\crawl\poems.sqlite";
        public const int DefaultDelayInterval = 500;

        public string DatabasePath { get; set; } = DefaultDatabasePath;
        public int DelayInterval { get; set; } = DefaultDelayInterval;
        public bool SkipPoetSearch { get; set; }
        public bool SkipPoemSearch { get; set; }

        public PoemsCommand()
        {
            IsCommand("Poems", "Crawl poemhunter.com");

            HasRequiredOption("d|database-path=", "The physical path to the database.", x => DatabasePath = x);
            HasOption("i|delay-interval", "The delay between requests (in ms).", x => DelayInterval = int.Parse(x));
            HasOption("skip-poet-search:", "Skip search for new poet slugs.", x => SkipPoetSearch = x == null || Convert.ToBoolean(x));
            HasOption("skip-poem-search:", "Skip search for new poem slugs.", x => SkipPoemSearch = x == null || Convert.ToBoolean(x));
        }

        protected override void RunAction(string[] remainingArguments)
        {
            using (var context = new PoemsContext(this))
            {
                Task.Run(async () =>
                {
                    await context.Crawl();
                }).GetAwaiter().GetResult();
            }
        }
    }
}
