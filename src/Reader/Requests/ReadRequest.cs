using System;
using System.Threading;
using System.Threading.Tasks;

namespace Reader.Requests
{
    /// <summary>
    /// Represents a reading part of a request/response message pattern.
    /// A complete response end the request.
    /// </summary>
    public sealed class ReadRequest<T> : IReadRequest<T>
    {
        private readonly IReaderTopic topic;
        private readonly T[] resultSegments;
        private readonly CancellationToken cancellationToken;
        private readonly TaskCompletionSource<T[]> taskCompletionSource;

        private int resultSegmentsCollected;

        internal ReadRequest(IReaderTopic topic, TaskCompletionSource<T[]> taskCompletionSource, T[] segments, CancellationToken cancellationToken)
        {
            this.topic = topic;
            this.taskCompletionSource = taskCompletionSource;
            this.cancellationToken = cancellationToken;
            this.resultSegments = segments;
            this.resultSegmentsCollected = 0;
        }

        /// <inheritdoc/>
        IReaderTopic IReadRequest<T>.Topic => this.topic;

        /// <inheritdoc/>
        bool IReadRequest<T>.CanComplete => this.resultSegments.Length <= this.resultSegmentsCollected;

        /// <inheritdoc/>
        bool IReadRequest<T>.IsCancellationRequested => this.cancellationToken.IsCancellationRequested;

        /// <inheritdoc/>
        void IReadRequest<T>.SetCanceled() => this.taskCompletionSource.SetCanceled(this.cancellationToken);

        /// <inheritdoc/>
        void IReadRequest<T>.AddResponseSegment(T data)
        {
            this.resultSegments[this.resultSegmentsCollected] = data;
            this.resultSegmentsCollected++;
        }

        /// <inheritdoc/>
        void IReadRequest<T>.Complete() => this.taskCompletionSource.SetResult(this.resultSegments);

        /// <inheritdoc/>
        void IReadRequest<T>.SetException(Exception exception) => this.taskCompletionSource.SetException(exception);
    }
}