using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MarkItDown.Desktop.Interop;

namespace MarkItDown.Desktop.Services;

public sealed class WorkerClient(IAppLog log) : IWorkerClient
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;

    public async Task<WorkerConversionResult> ConvertAsync(
        Guid requestId,
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var process = await EnsureProcessAsync(cancellationToken);
            var request = new WorkerRequest(
                WorkerProtocol.Version,
                requestId.ToString("D"),
                "convert",
                Path.GetFullPath(inputPath),
                AppPaths.CacheDirectory);

            await process.StandardInput.WriteLineAsync(
                JsonSerializer.Serialize(request, AppJsonSerializerContext.Default.WorkerRequest));
            await process.StandardInput.FlushAsync(cancellationToken);

            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    ResetProcess();
                    throw new WorkerException("worker_crashed", "The conversion worker stopped unexpectedly.");
                }

                WorkerResponse? response;
                try
                {
                    response = JsonSerializer.Deserialize(
                        line,
                        AppJsonSerializerContext.Default.WorkerResponse);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (response is null || response.RequestId != request.RequestId)
                {
                    continue;
                }

                if (response.ProtocolVersion != WorkerProtocol.Version)
                {
                    ResetProcess();
                    throw new WorkerException("protocol_mismatch", "The bundled conversion worker is incompatible with this app.");
                }

                if (response.Event == "completed" && response.MarkdownPath is not null)
                {
                    return new WorkerConversionResult(
                        response.MarkdownPath,
                        response.Title,
                        response.ElapsedMs ?? 0);
                }

                if (response.Event == "error")
                {
                    throw new WorkerException(
                        response.ErrorCode ?? "conversion_failed",
                        response.Message ?? "The document could not be converted.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            ResetProcess();
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            ResetProcess();
            throw new WorkerException("worker_crashed", "The conversion worker stopped unexpectedly.", exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task CancelAsync()
    {
        ResetProcess();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        ResetProcess();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Process> EnsureProcessAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false })
        {
            return _process;
        }

        AppPaths.EnsureCreated();
        CleanupStaleCacheFiles();

        var bundledPython = Path.Combine(AppContext.BaseDirectory, "Runtime", "Python", "python.exe");
        var workerScript = Path.Combine(AppContext.BaseDirectory, "Runtime", "Worker", "markitdown_worker.py");
        if (!File.Exists(bundledPython) || !File.Exists(workerScript))
        {
            throw new WorkerException("worker_missing", "The bundled conversion runtime is missing. Repair the installation.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = bundledPython,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            WorkingDirectory = AppContext.BaseDirectory,
        };
        startInfo.ArgumentList.Add("-I");
        startInfo.ArgumentList.Add(workerScript);
        var exifTool = Path.Combine(AppContext.BaseDirectory, "Runtime", "ExifTool", "exiftool.exe");
        if (File.Exists(exifTool))
        {
            startInfo.Environment["EXIFTOOL_PATH"] = exifTool;
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                throw new WorkerException("worker_start_failed", "The conversion worker could not be started.");
            }

            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    log.Error("worker_stderr", "worker_diagnostic");
                }
            };
            process.BeginErrorReadLine();

            var readyLine = await process.StandardOutput.ReadLineAsync(cancellationToken);
            WorkerResponse? ready = null;
            if (readyLine is not null)
            {
                try
                {
                    ready = JsonSerializer.Deserialize(
                        readyLine,
                        AppJsonSerializerContext.Default.WorkerResponse);
                }
                catch (JsonException)
                {
                    // Handled as a failed protocol handshake below.
                }
            }

            if (ready?.Event != "ready" || ready.ProtocolVersion != WorkerProtocol.Version)
            {
                throw new WorkerException("protocol_mismatch", "The conversion worker did not complete its startup handshake.");
            }

            _process = process;
            return process;
        }
        catch (WorkerException)
        {
            StopProcess(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            StopProcess(process);
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or Win32Exception)
        {
            StopProcess(process);
            throw new WorkerException(
                "worker_start_failed",
                "The bundled conversion worker could not be started. Repair the installation.",
                exception);
        }
    }

    private void ResetProcess()
    {
        var process = Interlocked.Exchange(ref _process, null);
        if (process is null)
        {
            return;
        }

        StopProcess(process);
    }

    private static void StopProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch
        {
            // The process may have exited between the checks.
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void CleanupStaleCacheFiles()
    {
        foreach (var file in Directory.EnumerateFiles(AppPaths.CacheDirectory, "*.md*"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // A locked cache file is safe to retry on the next launch.
            }
        }
    }
}
