using System;

namespace Reader
{
    public interface INotifyingReader<T>
    {
        Action DataAvailable { set; }

        bool TryRead(out (IReaderTopic topic, T data) data);
    }
}