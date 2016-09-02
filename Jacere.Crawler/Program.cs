using System;
using System.Threading.Tasks;

namespace Jacere.Crawler
{
    class Program
    {
        static void Main(string[] args)
        {
            //Recipes.Recipes.Start();

            Task.Run(async () =>
            {
                await Recipes.Recipes.CreateSubmissions();
            }).GetAwaiter().GetResult();
        }
    }
}
