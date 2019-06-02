﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using static LibHac.Results;
using static LibHac.Kvdb.ResultsKvdb;

namespace LibHac.Kvdb
{
    public class KeyValueDatabase<TKey, TValue>
        where TKey : IComparable<TKey>, IComparable, IEquatable<TKey>, IExportable, new()
        where TValue : IExportable, new()
    {
        private Dictionary<TKey, TValue> KvDict { get; } = new Dictionary<TKey, TValue>();

        public Result ReadDatabaseFromBuffer(ReadOnlySpan<byte> data)
        {
            var reader = new ImkvdbReader(data);

            Result headerResult = reader.ReadHeader(out int entryCount);
            if (headerResult.IsFailure()) return headerResult;

            for (int i = 0; i < entryCount; i++)
            {
                Result entryResult = reader.ReadEntry(out ReadOnlySpan<byte> keyBytes, out ReadOnlySpan<byte> valueBytes);
                if (entryResult.IsFailure()) return entryResult;

                var key = new TKey();
                var value = new TValue();

                key.FromBytes(keyBytes);
                value.FromBytes(valueBytes);

                KvDict.Add(key, value);
            }

            return ResultSuccess;
        }

        public Result WriteDatabaseToBuffer(Span<byte> output)
        {
            var writer = new ImkvdbWriter(output);

            writer.WriteHeader(KvDict.Count);

            foreach (KeyValuePair<TKey, TValue> entry in KvDict.OrderBy(x => x.Key))
            {
                writer.WriteEntry(entry.Key, entry.Value);
            }

            return ResultSuccess;
        }

        public int GetExportedSize()
        {
            int size = Unsafe.SizeOf<ImkvdbHeader>();

            foreach (KeyValuePair<TKey, TValue> entry in KvDict)
            {
                size += Unsafe.SizeOf<ImkvdbEntryHeader>();
                size += entry.Key.ExportSize;
                size += entry.Value.ExportSize;
            }

            return size;
        }
    }
}
