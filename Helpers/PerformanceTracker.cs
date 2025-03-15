using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PdfProcessingService.Helpers
{
    public class PerformanceTracker : IDisposable
    {
        private readonly Stopwatch _stopwatch;
        private readonly string _operationName;
        private readonly Action<string, double> _logAction;
        private readonly Dictionary<string, double>? _benchmarks;
        private bool _isDisposed;

        public PerformanceTracker(
            string operationName,
            Action<string, double> logAction,
            Dictionary<string, double> benchmarks = null)
        {
            _operationName = operationName;
            _logAction = logAction;
            _benchmarks = benchmarks;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _stopwatch.Stop();
                double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
                _logAction(_operationName, elapsedSeconds);

                if (_benchmarks != null)
                {
                    _benchmarks[_operationName] = elapsedSeconds;
                }

                _isDisposed = true;
            }
        }
    }
}