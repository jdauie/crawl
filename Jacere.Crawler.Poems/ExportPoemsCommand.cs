using System.Threading.Tasks;
using Jacere.Crawler.Core;

namespace Jacere.Crawler.Poems
{
    public class ExportPoemsCommand : ErrorHandlingConsoleComand
    {
        public const int DefaultDelayInterval = 500;

        public string SourcePath { get; set; }
        public string OutputPath { get; set; }

        public ExportPoemsCommand()
        {
            IsCommand("ExportPoems", "Export poems");

            HasRequiredOption("s|source-path=", "The physical path to the source database.", x => SourcePath = x);
            HasRequiredOption("o|output-path=", "The physical path to the output database.", x => OutputPath = x);
        }

        protected override void RunAction(string[] remainingArguments)
        {
            Task.Run(async () =>
            {
                await PoemsContext.Export(SourcePath, OutputPath);
            }).GetAwaiter().GetResult();
        }
    }
}
