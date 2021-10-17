using System;

namespace Reader
{
    public interface INotifyingReader<T>
    {
        Action DataAvailable { set; }

        bool TryRead(out (string topic, T data) data);
    }
}