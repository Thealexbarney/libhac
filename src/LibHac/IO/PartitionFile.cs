﻿using System;

namespace LibHac.IO
{
    public class PartitionFile : FileBase
    {
        private IStorage BaseStorage { get; }
        public long Offset { get; }
        private long Size { get; }
        public Validity Validity { get; } = Validity.Unchecked;

        public PartitionFile(IStorage baseStorage, long offset, long size , Validity validity, OpenMode mode)
        {
            Mode = mode;
            BaseStorage = baseStorage;
            Offset = offset;
            Size = size;
            Validity = validity;
        }

        public override int Read(Span<byte> destination, long offset)
        {
            int toRead = ValidateReadParamsAndGetSize(destination, offset);

            long storageOffset = Offset + offset;
            BaseStorage.Read(destination.Slice(0, toRead), storageOffset);

            return toRead;
        }

        public override void Write(ReadOnlySpan<byte> source, long offset)
        {
            throw new NotImplementedException();
        }

        public override void Flush()
        {
        }

        public override long GetSize()
        {
            return Size;
        }

        public override void SetSize(long size)
        {
            throw new NotSupportedException();
        }
    }
}
