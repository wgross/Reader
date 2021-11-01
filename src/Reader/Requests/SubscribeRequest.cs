using System;
using System.Threading;

namespace Reader.Requests
{
    public sealed class SubscribeRequest<T> : IReadRequest<T>, IObservable<T>, IDisposable
    {
        private readonly IReaderTopic topic;
        private readonly CancellationToken cancellationToken;

        private IObserver<T> observer;

        public SubscribeRequest(IReaderTopic topic, CancellationToken cancellationToken)
        {
            this.topic = topic;
            this.cancellationToken = cancellationToken;
            this.observer = observer;
        }

        /// <inheritdoc/>
        public IReaderTopic Topic => this.topic;

        /// <inheritdoc/>
        public bool CanComplete { get; private set; }

        /// <inheritdoc/>
        public bool IsCancellationRequested { get; private set; }

        /// <inheritdoc/>
        public void SetCanceled()
        {
            this.IsCancellationRequested = true;
            this.CanComplete = true;
            this.observer.OnCompleted();
        }

        /// <inheritdoc/>
        public void Complete()
        {
            this.IsCancellationRequested = true;
            this.CanComplete = true;
            this.observer?.OnCompleted();
        }

        public void AddResponseSegment(T data) => this.observer?.OnNext(data);

        public IDisposable Subscribe(IObserver<T> observer)
        {
            this.observer = observer;
            return this;
        }

        public void SetException(Exception exception)
        {
            this.IsCancellationRequested = true;
            this.CanComplete = true;
            this.observer?.OnError(exception);
        }

        public void Dispose()
        {
            this.IsCancellationRequested = true;
            this.CanComplete = true;
            this.observer?.OnCompleted();
        }
    }
}