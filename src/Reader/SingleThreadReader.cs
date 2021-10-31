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

        private sealed class ReadRequest
        {
            private readonly T[] resultSegments;
            private readonly CancellationToken cancellationToken;
            private readonly TaskCompletionSource<T[]> taskCompletionSource;
            private int resultSegmentsCollected;

            internal ReadRequest(IReaderTopic topic, TaskCompletionSource<T[]> taskCompletionSource, T[] segments, CancellationToken cancellationToken)
            {
                this.Topic = topic;
                this.taskCompletionSource = taskCompletionSource;
                this.cancellationToken = cancellationToken;
                this.resultSegments = segments;
                this.resultSegmentsCollected = 0;
            }

            internal IReaderTopic Topic { get; }

            /// <summary>
            /// A read request is competed if all segments have been received.
            /// </summary>
            internal bool CanComplete => this.resultSegments.Length <= this.resultSegmentsCollected;

            /// <summary>
            /// The read request is canceled if its <see cref="CancellationToken"/> was
            /// canceled from the outside or by timeout
            /// </summary>
            internal bool IsCancellationRequested => this.cancellationToken.IsCancellationRequested;

            internal void SetCanceled() => this.taskCompletionSource.SetCanceled(this.cancellationToken);

            internal void AddResultSegment(T data)
            {
                this.resultSegments[this.resultSegmentsCollected] = data;
                this.resultSegmentsCollected++;
            }

            internal void SetResult() => this.taskCompletionSource.SetResult(this.resultSegments);
        }

        /// <summary>
        /// Request a topic from the underlying <see cref="INotifyingReader{T}"/>.
        /// The Task completes when the data arrives.
        /// </summary>
        public Task<T[]> RequestAsync(IReaderTopic topic, CancellationToken cancellationToken) => this.RequestAsync(topic, 1, cancellationToken);

        public Task<T[]> RequestAsync(IReaderTopic topic, int segments, CancellationToken cancellationToken)
        {
            if (this.cancellationTokenSource.IsCancellationRequested)
            {
                // don't accept further read request if the reader is in state 'canceled'
                throw new InvalidOperationException($"ReadRequest(topic='{topic}') rejected: Reader is already disposed.");
            }

            if (segments <= 0)
            {
                throw new ArgumentException($"ReadRequest(topic='{topic}') rejected: number of segments mustn't be zero.", nameof(segments));
            }

            // the task completion source is used to block the caller until the response was read.
            // also the caller may mark the request as canceled.
            var taskCompletionSource = new TaskCompletionSource<T[]>();

            var readRequest = new ReadRequest(topic, taskCompletionSource, new T[segments], cancellationToken);

            // the request is added to the collection of pending requests.
            // one for each topic.
            if (!this.pendingReadRequests.TryAdd(topic, readRequest))
            {
                taskCompletionSource.SetException(new InvalidOperationException($"Topic('{topic}') is already pending"));
            }

            return taskCompletionSource.Task;
        }

        private void ReaderLoop(object _)
        {
            try
            {
                // the reader loop will block on the collection until a message is put into.
                // incomingMessages.CompleteAdding() isn't called in this scenario. The underlying reader
                // might need to call it to show it stopped working.
                BlockingCollection<(IReaderTopic topic, T data)> incomingMessages = new();

                this.underlyingReader.DataAvailable = (topic, data) =>
                {
                    // the notification handler appends the message to the blocking collection.
                    // this is the most minimal processing of the incoming data and should allow the underlying reader
                    // to continue receiving data quickly.
                    incomingMessages.Add((topic, data));
                };

                // the reader loop runs as long a cancellation of the read wasn't requested.
                while (!this.cancellationTokenSource.IsCancellationRequested)
                {
                    // incoming message blocks the loop from running. The lock is lifted every 500ms
                    // to allow cleanup and inspection of the cancellation token of the reader thread

                    if (incomingMessages.TryTake(out var incomingMessage, TimeSpan.FromMilliseconds(500)))
                    {
                        // before reading any data all canceled read requests are abandoned.
                        this.CleanupPendingRequests();

                        // now the incoming data is processed.
                        this.ProcessIncomingMessage(incomingMessage);
                    }
                    else
                    {
                        // The 'TryTake' operation unblocked without reading an item.
                        // this is cleanup 'only'
                        this.CleanupPendingRequests();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // this exception is suppressed.
                // its the expected way to end the loop from the outside.
            }
            finally
            {
                // unregister the callback from the underlying reader
                // this is necessary to allow garbage collection of this instance.
                this.underlyingReader.DataAvailable = null;
            }

            // TODO: log the end of the reader loop
            // TODO: log unexpected exceptions that break the reader loop.
        }

        private void ProcessIncomingMessage((IReaderTopic topic, T data) incomingMessage)
        {
            // find the pending read request and set its 'result'. This will unblock the
            // task completion source and the waiting thread may proceed with processing the result.
            // incoming data is dropped if no read request is pending.
            if (this.pendingReadRequests.TryGetValue(incomingMessage.topic, out var pendingReadRequest))
            {
                pendingReadRequest.AddResultSegment(incomingMessage.data);

                if (pendingReadRequest.CanComplete)
                {
                    // the result segment finished the pending read request.
                    // remove it from the collection of pending requests first..

                    this.pendingReadRequests.TryRemove(incomingMessage.topic, out var _);

                    // .. then unblock the waiting thread
                    pendingReadRequest.SetResult();
                }
            }
            else
            {
                // TODO: log here that unknown topic was dropped
            }
        }

        private void CleanupPendingRequests()
        {
            foreach (var pendingRequest in this.pendingReadRequests.Values.ToArray())
            {
                if (pendingRequest.IsCancellationRequested)
                {
                    // cancel the task completion source
                    pendingRequest.SetCanceled();

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

                    // cancel all read requests still pending to unlock the waiting threads.
                    foreach (var pendingRequest in this.pendingReadRequests.Values.ToArray())
                    {
                        pendingRequest.SetCanceled();
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