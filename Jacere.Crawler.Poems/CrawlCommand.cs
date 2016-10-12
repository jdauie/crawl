using Jacere.Crawler.Core;

namespace Jacere.Crawler.Poems
{
    public class PoemsCommand : ErrorHandlingConsoleComand
    {
        public const string DefaultDatabasePath = @"C:\tmp\crawl\poems.sqlite";
        
        public string DatabasePath { get; set; } = DefaultDatabasePath;

        public PoemsCommand()
        {
            IsCommand("Poems", "Crawl poemhunter.com");
            IsAdmin();
            
            HasOption("d|database-path=", "The physical path to the database.", x => DatabasePath = x);
        }

        protected override void RunAction(string[] remainingArguments)
        {
        }
    }
}
