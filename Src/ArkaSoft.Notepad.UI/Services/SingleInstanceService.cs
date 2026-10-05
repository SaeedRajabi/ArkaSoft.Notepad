using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace ArkaSoft.Notepad.UI.Services;

/// <summary>
/// Keeps the app single-instance: a second launch forwards its file arguments
/// to the running instance over a named pipe and exits, so files opened via
/// "Open with" become tabs of the existing window (Windows 11 Notepad behavior).
/// </summary>
public static class SingleInstanceService
{
    private const string MutexName = @"Local\ArkaSoft.Notepad.SingleInstance";
    private const string PipeName = "ArkaSoft.Notepad.Pipe";

    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        return createdNew;
    }

    /// <summary>Starts the pipe server that receives forwarded files. Runs until the app exits.</summary>
    public static void StartServer(Action<string[]> onFilesRequested, Action onActivateRequested)
    {
        var captured = SynchronizationContext.Current;
        void Post(Action action)
        {
            if (captured is not null)
                captured.Post(_ => action(), null);
            else if (System.Windows.Application.Current is { } app)
                app.Dispatcher.Invoke(action);
        }

        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                    server.WaitForConnection();

                    string payload;
                    using (var reader = new StreamReader(server, Encoding.UTF8))
                        payload = reader.ReadToEnd();

                    var files = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (files.Length > 0)
                        Post(() => onFilesRequested(files));
                    else
                        Post(onActivateRequested);
                }
                catch
                {
                    // broken connection or shutdown: keep serving
                    Thread.Sleep(100);
                }
            }
        })
        {
            IsBackground = true,
            Name = "SingleInstancePipe"
        };
        thread.Start();
    }

    /// <summary>Sends the given file paths to the already running instance.
    /// Returns false when no instance answered in time.</summary>
    public static bool ForwardToRunningInstance(IReadOnlyList<string> files)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 4000);
            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.Write(string.Join("\n", files));
            writer.Flush();
            client.WaitForPipeDrain();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
