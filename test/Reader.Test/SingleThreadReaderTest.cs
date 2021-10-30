using Moq;
using Reader.Topics;
using System;
using System.Collections.Generic;
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
            public Action<IReaderTopic, T> DataAvailable { set; private get; }

            private Stack<(IReaderTopic, T)> data = new();

            public void SetData(IReaderTopic topic, T data) => this.DataAvailable(topic, data);
        }

        private static IReaderTopic Topic(string value) => new StringTopic(value);

        [Fact]
        public async Task Read_data_and_await_completion()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = new SingleThreadReader<int>(lowLevelReader);
            await Task.Delay(10);

            // ACT
            var result = reader.RequestAsync(Topic("topic"), CancellationToken.None);

            lowLevelReader.SetData(Topic("topic"), 10);

            // ASSERT
            Assert.Equal(10, await result);
        }

        [Fact]
        public async Task Read_data_and_await_cancellation()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = new SingleThreadReader<int>(lowLevelReader);
            await Task.Delay(10);

            // ACT
            var cts = new CancellationTokenSource();
            var result = reader.RequestAsync(Topic("topic"), cts.Token);
            cts.Cancel();

            // ASSERT
            await Assert.ThrowsAsync<TaskCanceledException>(() => result);
        }

        [Fact]
        public async Task Read_data_and_await_timeout()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = new SingleThreadReader<int>(lowLevelReader);
            await Task.Delay(10);

            // ACT
            var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var result = reader.RequestAsync(Topic("topic"), cts.Token);

            // ASSERT
            await Assert.ThrowsAsync<TaskCanceledException>(() => result);
        }

        [Fact]
        public async Task Reject_request_for_already_pending_topic()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = new SingleThreadReader<int>(lowLevelReader);

            await Task.Delay(10);

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

            using var reader = new SingleThreadReader<int>(lowLevelReader);
            await Task.Delay(10);

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

            Assert.Equal(11, awaitResult[0]);
            Assert.Equal(12, awaitResult[1]);
        }

        [Fact]
        public async Task Read_different_topics_twice()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = new SingleThreadReader<int>(lowLevelReader);
            await Task.Delay(10);

            var first = reader.RequestAsync(Topic("topic1"), CancellationToken.None);
            lowLevelReader.SetData(Topic("topic1"), 12);
            await first;

            // ACT
            var result = reader.RequestAsync(Topic("topic2"), CancellationToken.None);

            lowLevelReader.SetData(Topic("topic2"), 11);

            // ASSERT
            var awaitResult = await result;

            Assert.Equal(11, awaitResult);
        }

        [Fact]
        public async Task Ignore_unkown_topic()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = new SingleThreadReader<int>(lowLevelReader);
            await Task.Delay(10);

            lowLevelReader.SetData(Topic("unknown"), 12);

            // ACT
            var result = reader.RequestAsync(Topic("topic2"), CancellationToken.None);

            lowLevelReader.SetData(Topic("topic2"), 11);

            // ASSERT
            var awaitResult = await result;

            Assert.Equal(11, awaitResult);
        }

        [Fact]
        public async Task Dispose_cancels_pending_requests()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            var reader = new SingleThreadReader<int>(lowLevelReader);

            await Task.Delay(10);

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

            var reader = new SingleThreadReader<int>(lowLevelReader);
            reader.Dispose();

            // ACT
            var result = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.RequestAsync(Topic("topic1"), CancellationToken.None));

            // ASSERT
            Assert.Equal("ReadRequest(topic='topic1') rejected: Reader is already disposed.", result.Message);
        }
    }
}