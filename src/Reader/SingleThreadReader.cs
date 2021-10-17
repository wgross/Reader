using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reader
{
    public sealed class SingleThreadReader<T> : IDisposable
    {
        private readonly INotifyingReader<T> underlyingReader;
        private readonly Thread readerThread;
        private readonly ConcurrentDictionary<string, ReadRequest> pendingReadRequests;

        private readonly CancellationTokenSource cancellationTokenSource = new();

        public SingleThreadReader(INotifyingReader<T> instance)
        {
            this.underlyingReader = instance;
            this.pendingReadRequests = new ConcurrentDictionary<string, ReadRequest>();
            this.dataAvailableEvent = new ManualResetEventSlim();
            this.readerThread = new Thread(this.ReaderLoop);
            this.readerThread.IsBackground = true;
            this.readerThread.Start();
        }

        private record ReadRequest(string Topic, TaskCompletionSource<T> TaskCompletionSource, CancellationToken CancellationToken);

        /// <summary>
        /// Request a topic from the underlying <see cref="INotifyingReader{T}"/>.
        /// The Task completes when the data arrives.
        /// </summary>
        public Task<T> RequestAsync(string topic, CancellationToken cancellationToken)
        {
            if (this.cancellationTokenSource.IsCancellationRequested)
            {
                throw new InvalidOperationException($"ReadRequest(topic='{topic}') rejected: Reader is already disposed.");
            }

            var taskCompletionSource = new TaskCompletionSource<T>();
            this.pendingReadRequests.TryAdd(topic, new ReadRequest(
                Topic: topic,
                TaskCompletionSource: taskCompletionSource,
                CancellationToken: cancellationToken));

            return taskCompletionSource.Task;
        }

        private void ReaderLoop(object obj)
        {
            try
            {
                var isDataAvailable = new ManualResetEventSlim();

                // if the read notifies the manual reset event is set and unlocks the
                // reader loop
                // TODO: log is event is set in state 'IsSet'.
                // This would be the rare case where data comes in after the reading and before the reset of the event.
                this.underlyingReader.DataAvailable = () => isDataAvailable.Set();

                // the reader loop runs as long a cancellation of the read wasn't requested.
                while (!this.cancellationTokenSource.IsCancellationRequested)
                {
                    // block the reader lop from running.
                    isDataAvailable.Wait(
                        // the lock is removed every 500 ms to remove canceled requests
                        timeout: TimeSpan.FromMilliseconds(500),
                        // canceling the SingleThreadReader instance also cancels the dataAvailableEvent
                        cancellationToken: this.cancellationTokenSource.Token);

                    // before reading any data all canceled read requests are abandoned.
                    this.CleanupPendingRequests();

                    // process pending reads
                    this.ReadAllPendingData();

                    // Between the end of reading and the resetting of the event data might have come in.
                    // It wouldn't be read because the event is already set. But b/c the event has a timeout of 500 ms
                    // it would be read later.

                    // lock the loop again
                    isDataAvailable.Reset();
                }
            }
            catch (OperationCanceledException)
            { }
        }

        private void ReadAllPendingData()
        {
            while (this.underlyingReader.TryRead(out var readData))
            {
                if (this.pendingReadRequests.TryRemove(readData.topic, out var pendingReadRequest))
                {
                    pendingReadRequest.TaskCompletionSource.SetResult(readData.data);
                }
                // TODO: log here that unknown topic was dropped
            }
        }

        private void CleanupPendingRequests()
        {
            foreach (var pendingRequest in this.pendingReadRequests.Values.ToArray())
            {
                if (pendingRequest.CancellationToken.IsCancellationRequested)
                {
                    // cancel the task completion source
                    pendingRequest.TaskCompletionSource.SetCanceled(pendingRequest.CancellationToken);

                    // and abandon the request
                    this.pendingReadRequests.TryRemove(pendingRequest.Topic, out var _);
                }
            }
        }

        #region IDisposable

        private void Dispose(bool disposing)
        {
            if (!this.cancellationTokenSource.IsCancellationRequested)
            {
                if (disposing)
                {
                    // cancel the tread
                    this.cancellationTokenSource.Cancel();

                    // wait for the execution flow to join the current thread
                    this.readerThread.Join();

                    // cancel all read requests still pending
                    foreach (var pendingRequest in this.pendingReadRequests.Values.ToArray())
                    {
                        pendingRequest.TaskCompletionSource.SetCanceled(pendingRequest.CancellationToken);
                    }
                    this.pendingReadRequests.Clear();
                }
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion IDisposable
    }
}