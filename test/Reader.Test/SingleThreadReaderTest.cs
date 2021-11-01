using Moq;
using Reader.Topics;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reader.Test
{
    public class SingleThreadReaderTest
    {
        private readonly MockRepository mocks = new(MockBehavior.Strict);

        public class ReaderMock<T> : INotifyingReader<T>
        {
            public Action<IReaderTopic, T> DataAvailable { set; get; }

            public void SetData(IReaderTopic topic, T data) => this.DataAvailable(topic, data);
        }

        private static IReaderTopic Topic(string value) => new StringTopic(value);

        [Fact]
        public async Task Dispose_unregisters_reader_event()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();
            var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            // ACT
            reader.Dispose();

            // ASSERT
            Assert.Null(lowLevelReader.DataAvailable);
        }

        [Fact]
        public async Task Dispose_unregisters_reader_event_multiple_loops()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();
            var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            // ACT
            await Task.Delay(1000);

            reader.Dispose();

            // ASSERT
            Assert.Null(lowLevelReader.DataAvailable);
        }

        [Fact]
        public async Task Read_data_and_await_completion()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            // ACT
            var result = reader.RequestAsync(Topic("topic"), CancellationToken.None);

            lowLevelReader.SetData(Topic("topic"), 10);

            // ASSERT
            Assert.Equal(10, (await result).Single());
        }

        [Fact]
        public async Task Read_segmented_data_and_await_completion()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            // ACT
            var result = reader.RequestAsync(Topic("topic"), segments: 2, CancellationToken.None);

            lowLevelReader.SetData(Topic("topic"), 10);
            lowLevelReader.SetData(Topic("topic"), 11);

            // ASSERT
            var awaitedResult = await result;

            Assert.Equal(2, awaitedResult.Length);
            Assert.Equal(10, awaitedResult.ElementAt(0));
            Assert.Equal(11, awaitedResult.ElementAt(1));
        }

        [Fact]
        public async Task Read_data_and_await_cancellation()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            var cancelReadRequest = new CancellationTokenSource();

            // ACT
            var result = reader.RequestAsync(Topic("topic"), cancelReadRequest.Token);
            cancelReadRequest.Cancel();

            // ASSERT
            await Assert.ThrowsAsync<TaskCanceledException>(() => result);
        }

        [Fact]
        public async Task Read_segmented_data_and_await_cancellation()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            var cancelReadRequest = new CancellationTokenSource();

            // ACT
            var result = reader.RequestAsync(Topic("topic"), segments: 2, cancelReadRequest.Token);

            lowLevelReader.SetData(Topic("topic"), 10);

            cancelReadRequest.Cancel();

            // ASSERT
            await Assert.ThrowsAsync<TaskCanceledException>(() => result);
        }

        [Fact]
        public async Task Read_data_and_await_timeout()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            var cancelReadRequest = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            // ACT
            var result = reader.RequestAsync(Topic("topic"), cancelReadRequest.Token);

            await Task.Delay(500);

            // ASSERT
            await Assert.ThrowsAsync<TaskCanceledException>(() => result);
        }

        [Fact]
        public async Task Read_segmented_data_and_await_timeout()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            var cancelReadRequest = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            // ACT
            var result = reader.RequestAsync(Topic("topic"), segments: 2, cancelReadRequest.Token);

            lowLevelReader.SetData(Topic("topic"), 10);

            await Task.Delay(500);

            // ASSERT
            await Assert.ThrowsAsync<TaskCanceledException>(() => result);
        }

        [Fact]
        public async Task Reject_request_for_already_pending_topic()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            var _ = reader.RequestAsync(Topic("topic1"), CancellationToken.None);

            // ACT
            var result = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.RequestAsync(Topic("topic1"), CancellationToken.None));

            // ASSERT
            Assert.Equal("Topic('topic1') is already pending", result.Message);
        }

        [Fact]
        public async Task Read_different_topics_in_parallel()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            // ACT
            var result = new[]
            {
                reader.RequestAsync(Topic("topic1"), CancellationToken.None),
                reader.RequestAsync(Topic("topic2"), CancellationToken.None)
            };

            lowLevelReader.SetData(Topic("topic2"), 12);
            lowLevelReader.SetData(Topic("topic1"), 11);

            // ASSERT
            var awaitResult = await Task.WhenAll(result);

            Assert.Equal(11, awaitResult[0].Single());
            Assert.Equal(12, awaitResult[1].Single());
        }

        [Fact]
        public async Task Read_different_topics_twice()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            var first = reader.RequestAsync(Topic("topic1"), CancellationToken.None);
            lowLevelReader.SetData(Topic("topic1"), 12);
            await first;

            // ACT
            var result = reader.RequestAsync(Topic("topic2"), CancellationToken.None);

            lowLevelReader.SetData(Topic("topic2"), 11);

            // ASSERT
            var awaitResult = await result;

            Assert.Equal(11, awaitResult.Single());
        }

        [Fact]
        public async Task Read_same_topic_twice()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            var first = reader.RequestAsync(Topic("topic1"), CancellationToken.None);
            lowLevelReader.SetData(Topic("topic1"), 12);
            await first;

            // ACT
            var result = reader.RequestAsync(Topic("topic1"), CancellationToken.None);

            lowLevelReader.SetData(Topic("topic1"), 11);

            // ASSERT
            var awaitResult = await result;

            Assert.Equal(11, awaitResult.Single());
        }

        [Fact]
        public async Task Ignore_unkown_topic()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            lowLevelReader.SetData(Topic("unknown"), 12);

            // ACT
            var result = reader.RequestAsync(Topic("topic2"), CancellationToken.None);

            lowLevelReader.SetData(Topic("topic2"), 11);

            // ASSERT
            var awaitResult = await result;

            Assert.Equal(11, awaitResult.Single());
        }

        [Fact]
        public async Task Dispose_cancels_pending_requests()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            var cts1 = new CancellationTokenSource();
            var cts2 = new CancellationTokenSource();
            var result = new[]
            {
                reader.RequestAsync(Topic("topic1"), cts1.Token),
                reader.RequestAsync(Topic("topic2"), cts2.Token)
            };

            // ACT
            reader.Dispose();

            // ASSERT
            var awaitResult = Task.WhenAll(result);

            Assert.All(result, r => Assert.True(r.IsCanceled));
        }

        [Fact]
        public async Task Reject_requests_when_disposed()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);
            reader.Dispose();

            // ACT
            var result = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.RequestAsync(Topic("topic1"), CancellationToken.None));

            // ASSERT
            Assert.Equal("ReadRequest(topic='topic1') rejected: Reader is already disposed.", result.Message);
        }

        [Fact]
        public async Task Reject_zero_segments()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            // ACT
            var result = await Assert.ThrowsAsync<ArgumentException>(() => reader.RequestAsync(Topic("topic1"), segments: 0, CancellationToken.None));

            // ASSERT
            Assert.Equal("segments", result.ParamName);
            Assert.Equal("ReadRequest(topic='topic1') rejected: number of segments mustn't be zero. (Parameter 'segments')", result.Message);
        }

        [Fact]
        public async Task Subscribe_data()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            var observer = new ObserverMock();

            // ACT
            using var result = await reader.SubscribeAsync(Topic("topic"), observer, CancellationToken.None);

            lowLevelReader.SetData(Topic("topic"), 10);

            await Task.Delay(10);

            // ASSERT
            Assert.Equal(10, observer.Data.Single());
        }

        [Fact]
        public async Task Subscribe_multiple_segments()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            var observer = new ObserverMock();

            // ACT
            using var result = await reader.SubscribeAsync(Topic("topic"), observer, CancellationToken.None);

            lowLevelReader.SetData(Topic("topic"), 10);
            lowLevelReader.SetData(Topic("topic"), 11);

            await Task.Delay(10);

            // ASSERT
            Assert.Equal(new[] { 10, 11 }, observer.Data);
        }

        [Fact]
        public async Task Subscriptions_Dispose_ends_subscription()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = await SingleThreadReaderFactory.CreateAsync<int>(lowLevelReader);

            var observer = new ObserverMock();

            // ACT
            using (var result = await reader.SubscribeAsync(Topic("topic"), observer, CancellationToken.None))
            {
                lowLevelReader.SetData(Topic("topic"), 10);

                await Task.Delay(10);
            }

            lowLevelReader.SetData(Topic("topic"), 11);

            await Task.Delay(10);

            // ASSERT
            Assert.Equal(new[] { 10 }, observer.Data);
        }
    }
}