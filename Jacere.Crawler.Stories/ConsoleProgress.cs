using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Jacere.Crawler.Stories
{
    public class ConsoleProgress : IDisposable
    {
        private readonly string _title;
        private readonly DateTime _startTime;
        private readonly int _totalCount;
        private int _progressCount;

        public ConsoleProgress(string title, int totalCount = 0)
        {
            _title = title;
            _startTime = DateTime.UtcNow;
            _totalCount = totalCount;
            _progressCount = 0;
        }

        private IEnumerable<string> GetRemainingTimeEstimate(int progressCount)
        {
            var elapsed = DateTime.UtcNow - _startTime;
            var itemsPerMinute = (int)(progressCount / elapsed.TotalMinutes);
            var parts = new List<string>
            {
                $"{itemsPerMinute} items/m",
            };
            if (_progressCount <= _totalCount)
            {
                var remainingSeconds = elapsed.TotalSeconds*(_totalCount - progressCount)/progressCount;
                parts.Add($"{TimeSpan.FromSeconds(remainingSeconds).ToString(@"dd\.hh\:mm\:ss")} remaining");
            }
            else
            {
                parts.Add("unknown time remaining");
            }
            return parts;
        }

        public void Increment()
        {
            var progressCount = Interlocked.Increment(ref _progressCount);
            var additionalParts = new List<string>();
            if (_totalCount > 0)
            {
                additionalParts.AddRange(GetRemainingTimeEstimate(progressCount));
            }
            var additionalInfo = additionalParts.Any()
                ? $"({string.Join(", ", additionalParts)})"
                : "";
            Console.Write($"{new string(' ', 80)}\r");
            Console.Write($"{_title}: {progressCount} {additionalInfo}\r");
        }

        public void Dispose()
        {
            var totalTime = (DateTime.UtcNow - _startTime).ToString(@"dd\.hh\:mm\:ss");
            Console.Write($"{new string(' ', 80)}\r");
            Console.WriteLine($"{_title}: {_progressCount} in {totalTime}");
        }
    }
}
