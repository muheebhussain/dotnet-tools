using Microsoft.IO;

namespace ArchivalSystem.Infrastructure;

/// <summary>
/// A write-only stream used as a target for ParquetWriter that buffers to a pooled memory stream
/// and automatically spills to a temp file when a configured threshold is exceeded.
/// After writing completes call <see cref="GetReadStream"/> to obtain a read stream for upload.
/// The caller is responsible for disposing the returned read stream. Dispose() will clean up any
/// remaining resources (temp file if used).
/// </summary>
public sealed class SpillableStream : Stream, IDisposable
{
    private static readonly RecyclableMemoryStreamManager DefaultManager = new();

    private readonly RecyclableMemoryStreamManager _manager;
    private readonly long _thresholdBytes;
    private MemoryStream? _mem;
    private FileStream? _fileWrite;        // write-side file stream while writing after spill
    private string? _tempPath;
    private long _position;
    private bool _completed;               // writing finished
    private bool _readStreamTaken;         // owner transferred read stream

    public SpillableStream(long thresholdBytes = 16 * 1024 * 1024, RecyclableMemoryStreamManager? manager = null)
    {
        _manager = manager ?? DefaultManager;
        _thresholdBytes = Math.Max(1, thresholdBytes);
        _mem = _manager.GetStream();
        _position = 0;
        _completed = false;
        _readStreamTaken = false;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_completed;
    public override long Length => _completed ? (_fileWrite != null ? new FileInfo(_tempPath!).Length : _mem?.Length ?? 0) : _position;
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    // Write synchronously
    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_completed) throw new InvalidOperationException("Stream already completed for writing.");
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));

        // If not yet spilled and writing would exceed threshold, spill to file first.
        if (_fileWrite == null && (_position + count) > _thresholdBytes)
        {
            // Create temp file and copy mem -> file
            _tempPath = Path.Combine(Path.GetTempPath(), $"parquet_spill_{Guid.NewGuid():N}.tmp");
            _fileWrite = new FileStream(_tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

            if (_mem != null)
            {
                _mem.Position = 0;
                await _mem.CopyToAsync(_fileWrite, 81920, cancellationToken).ConfigureAwait(false);
                _mem.Dispose();
                _mem = null;
            }
        }

        if (_fileWrite != null)
        {
            await _fileWrite.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _mem!.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }

        _position += count;
    }

    public override void Flush()
    {
        FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_fileWrite != null)
        {
            await _fileWrite.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (_mem != null)
        {
            await _mem.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Finalize writing and return a read-only stream for uploading.
    /// The returned stream takes ownership of the underlying data and the SpillableStream will not
    /// attempt to dispose it again. Caller must dispose the returned stream after upload.
    /// </summary>
    public Stream GetReadStream()
    {
        if (_readStreamTaken) throw new InvalidOperationException("GetReadStream already called.");

        // finish writing
        _completed = true;

        if (_fileWrite != null)
        {
            // close write handle and open a read handle for upload
            _fileWrite.Dispose();
            _fileWrite = null;

            // Open read-stream that the caller will dispose.
            var readFs = new FileStream(_tempPath!, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);

            // Mark ownership transfer: SpillableStream will not delete file until Dispose, but we will return read stream.
            _readStreamTaken = true;
            return readFs;
        }
        else if (_mem != null)
        {
            // Reset position and hand over the recyclable memory stream to caller.
            _mem.Position = 0;
            var readMem = _mem;
            // prevent double-dispose: null out internal reference so Dispose won't dispose it
            _mem = null;
            _readStreamTaken = true;
            return readMem;
        }
        else
        {
            // no data: return empty stream
            _readStreamTaken = true;
            return new MemoryStream(Array.Empty<byte>(), writable: false);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // If read stream was not taken and mem exists, dispose it to return buffers.
            try
            {
                _mem?.Dispose();
            }
            catch { }

            // If there is a leftover write file, dispose and delete it.
            try
            {
                _fileWrite?.Dispose();
            }
            catch { }

            if (_tempPath != null)
            {
                try { File.Delete(_tempPath); } catch { /* ignore */ }
                _tempPath = null;
            }
        }
        base.Dispose(disposing);
    }

    // Not supported operations
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}