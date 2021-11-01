using System.Threading.Tasks;

namespace Reader
{
    public static class SingleThreadReaderFactory
    {
        public static Task<SingleThreadReader<T>> CreateAsync<T>(INotifyingReader<T> underlyingReader)
        {
            var threadStarted = new TaskCompletionSource<SingleThreadReader<T>>();

            new SingleThreadReader<T>(underlyingReader, threadStarted);

            return threadStarted.Task;
        }
    }
}