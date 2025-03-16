using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PdfProcessingService.Helpers
{
    /// <summary>
    /// Utility class for tracking and recording the execution time of operations.
    /// </summary>
    public class PerformanceTracker : IDisposable
    {
        private readonly Stopwatch _stopwatch;
        private readonly string _operationName;
        private readonly Action<string, double> _logAction;
        private readonly Dictionary<string, double>? _benchmarks;
        private bool _isDisposed;

        /// <summary>
        /// Initializes a new instance of the PerformanceTracker class.
        /// </summary>
        /// <param name="operationName">Name of the operation being tracked.</param>
        /// <param name="logAction">Action to execute for logging the timing results.</param>
        /// <param name="benchmarks">Optional dictionary to store benchmark results.</param>
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

        /// <summary>
        /// Stops timing the operation and records the elapsed time.
        /// </summary>
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