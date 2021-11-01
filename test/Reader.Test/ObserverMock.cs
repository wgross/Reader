using System;
using System.Collections.Generic;

namespace Reader.Test
{
    public class ObserverMock : IObserver<int>
    {
        public bool? IsCompleted { get; private set; }

        public List<int> Data { get; } = new();

        public Exception Error { get; private set; }

        public void OnCompleted() => this.IsCompleted = true;

        public void OnError(Exception error) => this.Error = error;

        public void OnNext(int value) => this.Data.Add(value);
    }
}