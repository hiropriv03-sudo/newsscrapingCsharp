namespace NewsScraping.Utils;

public sealed class SingleInstanceMutex : IDisposable
{
    private readonly Mutex _mutex;
    private bool _hasHandle;

    public SingleInstanceMutex(string appName)
    {
        var mutexName = $"Global\\{appName}_SingleInstanceMutex";
        _mutex = new Mutex(true, mutexName, out _hasHandle);
    }

    public bool IsOnlyInstance => _hasHandle;

    public void Dispose()
    {
        if (_hasHandle)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 無視
            }
            _hasHandle = false;
        }
        _mutex.Dispose();
    }
}
