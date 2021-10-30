using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reader
{
    public sealed class SingleThreadReader<T> : IDisposable
    {
        /// <summary>
        /// The reader abstraction.
        /// </summary>
        private readonly INotifyingReader<T> underlyingReader;

        /// <summary>
        /// The actual reading thread. Processes sequentially all incoming data.
        /// </summary>
        private readonly Thread readerThread;

        /// <summary>
        /// holds the pending read requests. One for each topic.
        /// </summary>
        private readonly ConcurrentDictionary<IReaderTopic, ReadRequest> pendingReadRequests;

        /// <summary>
        /// If set cancels the readers operation
        /// </summary>
        private readonly CancellationTokenSource cancellationTokenSource = new();

        public SingleThreadReader(INotifyingReader<T> instance)
        {
            this.underlyingReader = instance;
            this.pendingReadRequests = new ConcurrentDictionary<IReaderTopic, ReadRequest>();
            this.readerThread = new Thread(this.ReaderLoop);
            this.readerThread.IsBackground = true;
            this.readerThread.Start();
        }

        private record ReadRequest(IReaderTopic Topic, TaskCompletionSource<T> TaskCompletionSource, CancellationToken CancellationToken);

        /// <summary>
        /// Request a topic from the underlying <see cref="INotifyingReader{T}"/>.
        /// The Task completes when the data arrives.
        /// </summary>
        public Task<T> RequestAsync(IReaderTopic topic, CancellationToken cancellationToken)
        {
            if (this.cancellationTokenSource.IsCancellationRequested)
            {
                // don't accept further read request if the reader is in state 'canceled'
                throw new InvalidOperationException($"ReadRequest(topic='{topic}') rejected: Reader is already disposed.");
            }

            // the task completion source is used to block the caller until the response was read.
            // also the caller may mark the request as canceled.
            var taskCompletionSource = new TaskCompletionSource<T>();

            var readRequest = new ReadRequest(
                Topic: topic,
                TaskCompletionSource: taskCompletionSource,
                CancellationToken: cancellationToken);

            // the request is added to the collection of pending requests.
            // one for each topic.
            if (!this.pendingReadRequests.TryAdd(topic, readRequest))
            {
                taskCompletionSource.SetException(new InvalidOperationException($"Topic('{topic}') is already pending"));
            }

            return taskCompletionSource.Task;
        }

        private void ReaderLoop(object obj)
        {
            try
            {
                var isDataAvailable = new ManualResetEventSlim();

                // if the read notifies the manual reset event is set and unlocks the
                // reader loop
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

                    // Reset the event before the reading starts to avoid the case that
                    // 1. reading is finished
                    // 2. new data comes in at underlying reader -> notifies: but event is already set
                    // 3. reset the event
                    isDataAvailable.Reset();

                    // process pending reads
                    this.ReadAllPendingData();
                }
            }
            catch (OperationCanceledException)
            { }
            // TODO: log the end of the reader loop
            // TODO: log unexpected exceptions that break the reader loop.
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