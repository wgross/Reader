namespace Reader
{
    public interface IReadRequest<T>
    {
        /// <summary>
        /// Indicates if the request has been canceled and may be removed from the collection of pending requests.
        /// </summary>
        bool IsCancellationRequested { get; }

        /// <summary>
        /// Describes the topic the request is waiting for.
        /// </summary>
        IReaderTopic Topic { get; }

        /// <summary>
        /// Cancels the request. This called by the reader when cleaning up during disposal.
        /// </summary>
        void SetCanceled();

        /// <summary>
        /// Adds a received piece of data to the request result.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        void AddResponseSegment(T data);

        /// <summary>
        /// After receiving data the read will ask if the request was satisfied and my be completed.
        /// </summary>
        bool CanComplete { get; }

        /// <summary>
        /// Called by the reader after all internal processing has been finished and the request was removed from the
        /// collection of pending requests.
        /// </summary>
        void Complete();
    }
}