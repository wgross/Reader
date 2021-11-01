using Reader.Requests;
using Reader.Topics;
using System;
using System.Linq;
using System.Threading;
using Xunit;

namespace Reader.Test
{
    public class SubscribeRequestTest
    {
        private static IReaderTopic Topic(string value) => new StringTopic(value);

        [Fact]
        public void Cancelling_ends_the_subscription()
        {
            // ARRANGE
            var request = new SubscribeRequest<int>(Topic("topic"), CancellationToken.None);
            var observer = new ObserverMock();

            request.Subscribe(observer);

            // ACT
            request.SetCanceled();

            // ASSERT
            Assert.True(observer.IsCompleted);
            Assert.True(request.CanComplete);
            Assert.True(request.IsCancellationRequested);
        }

        [Fact]
        public void Completing_ends_the_subscription()
        {
            // ARRANGE
            var request = new SubscribeRequest<int>(Topic("topic"), CancellationToken.None);
            var observer = new ObserverMock();

            request.Subscribe(observer);

            // ACT
            request.Complete();

            // ASSERT
            Assert.True(observer.IsCompleted);
            Assert.True(request.CanComplete);
            Assert.True(request.IsCancellationRequested);
        }

        [Fact]
        public void Completing_ignores_missing_subscription()
        {
            // ARRANGE
            var request = new SubscribeRequest<int>(Topic("topic"), CancellationToken.None);

            // ACT
            request.Complete();

            // ASSERT
            Assert.True(request.CanComplete);
            Assert.True(request.IsCancellationRequested);
        }

        [Fact]
        public void Disposing_ends_the_subscription()
        {
            // ARRANGE
            var request = new SubscribeRequest<int>(Topic("topic"), CancellationToken.None);
            var observer = new ObserverMock();

            request.Subscribe(observer);

            // ACT
            request.Dispose();

            // ASSERT
            Assert.True(observer.IsCompleted);
            Assert.True(request.CanComplete);
            Assert.True(request.IsCancellationRequested);
        }

        [Fact]
        public void Dispose_ignores_missing_subscription()
        {
            // ARRANGE
            var request = new SubscribeRequest<int>(Topic("topic"), CancellationToken.None);

            // ACT
            request.Dispose();

            // ASSERT
            Assert.True(request.CanComplete);
            Assert.True(request.IsCancellationRequested);
        }

        [Fact]
        public void AddResposeSegments_sends_data_to_observer()
        {
            // ARRANGE
            var request = new SubscribeRequest<int>(Topic("topic"), CancellationToken.None);
            var observer = new ObserverMock();

            request.Subscribe(observer);

            // ACT
            request.AddResponseSegment(10);

            // ASSERT
            Assert.Equal(10, observer.Data.Single());
        }

        [Fact]
        public void AddResposeSegments_ignores_missing_subscription()
        {
            // ARRANGE
            var request = new SubscribeRequest<int>(Topic("topic"), CancellationToken.None);
            var observer = new ObserverMock();

            // ACT
            request.AddResponseSegment(10);

            // ASSERT
            Assert.Empty(observer.Data);
        }

        [Fact]
        public void SetException_sends_the_error_and_completes_the_request()
        {
            // ARRANGE
            var request = new SubscribeRequest<int>(Topic("topic"), CancellationToken.None);
            var observer = new ObserverMock();

            request.Subscribe(observer);

            var exception = new Exception();

            // ACT
            request.SetException(exception);

            // ASSERT
            Assert.Same(exception, observer.Error);
            Assert.True(request.IsCancellationRequested);
            Assert.True(request.CanComplete);
        }

        [Fact]
        public void SetException_ignores_if_unsubscribed()
        {
            // ARRANGE
            var request = new SubscribeRequest<int>(Topic("topic"), CancellationToken.None);
            var observer = new ObserverMock();

            var exception = new Exception();

            // ACT
            request.SetException(exception);

            // ASSERT
            Assert.Null(observer.Error);
            Assert.True(request.IsCancellationRequested);
            Assert.True(request.CanComplete);
        }
    }
}