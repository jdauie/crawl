using System.Threading.Tasks;
using Jacere.Crawler.Core;

namespace Jacere.Crawler.Poems
{
    public class PoemsCommand : ErrorHandlingConsoleComand, IDataStorageCommand
    {
        public const string DefaultDatabasePath = @"C:\tmp\crawl\poems.sqlite";
        
        public string DatabasePath { get; set; } = DefaultDatabasePath;

        public PoemsCommand()
        {
            IsCommand("Poems", "Crawl poemhunter.com");
            
            HasOption("d|database-path=", "The physical path to the database.", x => DatabasePath = x);
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
