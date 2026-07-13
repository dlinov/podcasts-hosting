namespace PodcastsHosting.EndToEndTests;

internal sealed class DeterministicAudioStream : Stream
{
    private readonly byte[] _header;
    private readonly long _length;
    private readonly byte _payloadByte;
    private long _position;

    public DeterministicAudioStream(long length, byte[] header, byte payloadByte)
    {
        if (length < header.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length must fit the audio header.");
        }

        _length = length;
        _header = header;
        _payloadByte = payloadByte;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set
        {
            if (value < 0 || value > _length)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _position = value;
        }
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadInto(buffer.AsSpan(offset, count));
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ReadInto(buffer.Span));
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        Position = position;
        return _position;
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    private int ReadInto(Span<byte> buffer)
    {
        if (_position >= _length)
        {
            return 0;
        }

        var bytesToRead = (int)Math.Min(buffer.Length, _length - _position);
        var destination = buffer[..bytesToRead];
        destination.Fill(_payloadByte);

        if (_position < _header.Length)
        {
            var headerBytesToCopy = (int)Math.Min(bytesToRead, _header.Length - _position);
            _header.AsSpan((int)_position, headerBytesToCopy).CopyTo(destination);
        }

        _position += bytesToRead;
        return bytesToRead;
    }
}