using Moq;
using System;
using System.Collections.Generic;
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
            public Action DataAvailable { set; get; }

            private Stack<(string, T)> data = new();

            public void SetData(string topic, T data)
            {
                this.data.Push((topic, data));
                this.DataAvailable();
            }

            public bool TryRead(out (string topic, T data) readData)
            {
                if (this.data.Any())
                {
                    readData = this.data.Pop();
                    return true;
                }

                readData = default;
                return false;
            }
        }

        [Fact]
        public async Task Read_data_and_await_completion()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = new SingleThreadReader<int>(lowLevelReader);
            await Task.Delay(10);

            // ACT
            var result = reader.RequestAsync(topic: "topic", CancellationToken.None);

            lowLevelReader.SetData("topic", 10);

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
            var result = reader.RequestAsync(topic: "topic", cts.Token);
            cts.Cancel();

            // ASSERT
            await Assert.ThrowsAsync<TaskCanceledException>(() => result);
        }

        [Fact]
        public async Task Read_multiple_topics()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = new SingleThreadReader<int>(lowLevelReader);
            await Task.Delay(10);

            // ACT
            var result = new[]
            {
                reader.RequestAsync(topic: "topic1", CancellationToken.None),
                reader.RequestAsync(topic: "topic2", CancellationToken.None)
            };

            lowLevelReader.SetData("topic2", 12);
            lowLevelReader.SetData("topic1", 11);

            // ASSERT
            var awaitResult = await Task.WhenAll(result);

            Assert.Equal(11, awaitResult[0]);
            Assert.Equal(12, awaitResult[1]);
        }

        [Fact]
        public async Task Read_twice()
        {
            // ARRANGE
            var lowLevelReader = new ReaderMock<int>();

            using var reader = new SingleThreadReader<int>(lowLevelReader);
            await Task.Delay(10);

            var first = reader.RequestAsync(topic: "topic1", CancellationToken.None);
            lowLevelReader.SetData("topic1", 12);
            await first;

            // ACT
            var result = reader.RequestAsync(topic: "topic2", CancellationToken.None);

            lowLevelReader.SetData("topic2", 11);

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

            lowLevelReader.SetData("unknown", 12);

            // ACT
            var result = reader.RequestAsync(topic: "topic2", CancellationToken.None);

            lowLevelReader.SetData("topic2", 11);

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
                reader.RequestAsync(topic: "topic1", cts1.Token),
                reader.RequestAsync(topic: "topic2", cts2.Token)
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
            var result = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.RequestAsync(topic: "topic1", CancellationToken.None));

            // ASSERT
            Assert.Equal("ReadRequest(topic='topic1') rejected: Reader is already disposed.", result.Message);
        }
    }
}