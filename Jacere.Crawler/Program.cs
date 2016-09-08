using System;

namespace Jacere.Crawler
{
    class Program
    {
        static void Main(string[] args)
        {
            // keep-alive
            if (NativeMethods.SetThreadExecutionState(
                NativeMethods.ES_CONTINUOUS | NativeMethods.ES_SYSTEM_REQUIRED) == 0)
            {
                throw new Exception("failed to set execution state");
            }

            //Recipes.Recipes.Start();
            Stories.Stories.Start();
        }
    }
}
