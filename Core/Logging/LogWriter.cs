using System;
using System.Collections.Concurrent;
using System.Threading;

namespace GeminiV26.Core.Logging
{
    public sealed class LogWriter : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>(new ConcurrentQueue<Action>());
        private readonly Thread _worker;
        private volatile bool _disposed;

        public LogWriter()
        {
            _worker = new Thread(WorkLoop)
            {
                IsBackground = true,
                Name = "GeminiV26.LogWriter"
            };
            _worker.Start();
        }

        public void Enqueue(Action writeAction)
        {
            if (_disposed || writeAction == null)
                return;

            try
            {
                _queue.Add(writeAction);
            }
            catch (InvalidOperationException)
            {
                // queue completed
            }
        }

        private void WorkLoop()
        {
            foreach (var action in _queue.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch
                {
                    // swallow to keep background writer alive
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _queue.CompleteAdding();

            try
            {
                _worker.Join(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }

            _queue.Dispose();
        }
    }
}
