using System;

namespace Reader
{
    /// <summary>
    /// Abstraction of a reader which sends an event when data is received.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface INotifyingReader<T>
    {
        Action<IReaderTopic, T> DataAvailable { set; }
    }
}