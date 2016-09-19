using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jacere.Crawler.Core
{
    public class ConsoleProgress : IDisposable
    {
        private const int UpdateInterval = 100;

        private readonly string _title;
        private readonly DateTime _startTime;
        private readonly int _totalCount;
        private readonly bool _showRate;
        private readonly Dictionary<string, int> _counts;
        private readonly Task _task;
        private int _progressCount;
        private bool _dirty;
        private bool _disposing;

        public ConsoleProgress(string title, int totalCount = 0, bool showRate = true)
        {
            _title = title;
            _startTime = DateTime.UtcNow;
            _totalCount = totalCount;
            _showRate = showRate;
            _counts = new Dictionary<string, int>();
            _progressCount = 0;
            _dirty = false;
            _disposing = false;

            _task = UpdateDisplay();
        }

        private async Task UpdateDisplay()
        {
            while (!_disposing)
            {
                if (_dirty)
                {
                    Write();
                }
                await Task.Delay(UpdateInterval);
            }
        }

        private IEnumerable<string> GetRemainingTimeEstimate(int progressCount)
        {
            var elapsed = DateTime.UtcNow - _startTime;
            var itemsPerMinute = (int)(progressCount / elapsed.TotalMinutes);
            var parts = new List<string>();
            if (_showRate)
            {
                parts.Add($"{itemsPerMinute} items/m");
            }
            if (_progressCount <= _totalCount)
            {
                var remainingSeconds = elapsed.TotalSeconds * (_totalCount - progressCount) / progressCount;
                parts.Add($"{TimeSpan.FromSeconds(remainingSeconds):dd.hh:mm:ss} remaining");
            }
            else
            {
                parts.Add("unknown time remaining");
            }
            return parts;
        }

        public void Increment()
        {
            Interlocked.Increment(ref _progressCount);
            _dirty = true;
        }

        public void Add(int value)
        {
            Interlocked.Add(ref _progressCount, value);
            _dirty = true;
        }

        public void Set(int value)
        {
            Interlocked.Exchange(ref _progressCount, value);
            _dirty = true;
        }

        public void Increment(string name)
        {
            lock (_counts)
            {
                if (!_counts.ContainsKey(name))
                {
                    _counts[name] = 1;
                }
                else
                {
                    ++_counts[name];
                }
            }

            _dirty = true;
        }

        private void Write()
        {
            var progressCount = _progressCount;
            var additionalParts = new List<string>();

            _dirty = false;

            lock (_counts)
            {
                if (_counts.Count > 0)
                {
                    additionalParts.AddRange(_counts.Select(x => $@"{x.Value} {x.Key}"));
                }
            }

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
            if (_disposing)
            {
                return;
            }

            _disposing = true;
            _task.GetAwaiter().GetResult();
            var totalTime = DateTime.UtcNow - _startTime;

            Console.Write($"{new string(' ', 80)}\r");
            Console.WriteLine($"{_title}: {_progressCount} in {totalTime:dd.hh:mm:ss}");
        }
    }
}