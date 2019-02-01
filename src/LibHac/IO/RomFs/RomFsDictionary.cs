﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace LibHac.IO.RomFs
{
    internal class RomFsDictionary<T> where T : unmanaged
    {
        private int _count;
        private int _length;
        private int _capacity;

        private int[] Buckets { get; set; }
        private byte[] Entries { get; set; }

        // Hack around not being able to get the size of generic structures
        private readonly int _sizeOfEntry = 12 + Marshal.SizeOf<T>();

        public RomFsDictionary(IStorage bucketStorage, IStorage entryStorage)
        {
            Buckets = bucketStorage.ToArray<int>();
            Entries = entryStorage.ToArray();

            _length = Entries.Length;
            _capacity = Entries.Length;

            _count = CountEntries();
        }

        public RomFsDictionary(int capacity)
        {
            int size = HashHelpers.GetPrime(capacity);

            Buckets = new int[size];
            Buckets.AsSpan().Fill(-1);
            Entries = new byte[EstimateEntryTableSize(size)];
            _capacity = Entries.Length;
        }

        public ReadOnlySpan<int> GetBucketData() => Buckets.AsSpan();
        public ReadOnlySpan<byte> GetEntryData() => Entries.AsSpan(0, _length);

        public bool TryGetValue(ref RomEntryKey key, out RomKeyValuePair<T> value)
        {
            int i = FindEntry(ref key);

            if (i >= 0)
            {
                GetEntryInternal(i, out RomFsEntry<T> entry);

                value = new RomKeyValuePair<T> { Key = key, Value = entry.Value, Offset = i };
                return true;
            }

            value = default;
            return false;
        }

        public bool TryGetValue(int offset, out RomKeyValuePair<T> value)
        {
            if (offset < 0 || offset + _sizeOfEntry >= Entries.Length)
            {
                value = default;
                return false;
            }

            value = new RomKeyValuePair<T>();

            GetEntryInternal(offset, out RomFsEntry<T> entry, out value.Key.Name);
            value.Value = entry.Value;
            value.Key.Parent = entry.Parent;
            return true;
        }

        public bool TrySetValue(ref RomEntryKey key, ref T value)
        {
            int i = FindEntry(ref key);
            if (i < 0) return false;

            GetEntryInternal(i, out RomFsEntry<T> entry);
            entry.Value = value;
            SetEntryInternal(i, ref entry);

            return true;
        }

        public bool ContainsKey(ref RomEntryKey key) => FindEntry(ref key) >= 0;

        public int Insert(ref RomEntryKey key, ref T value)
        {
            if (ContainsKey(ref key))
            {
                throw new ArgumentException("Key already exists in dictionary.");
            }

            uint hashCode = key.GetRomHashCode();

            int bucket = (int)(hashCode % Buckets.Length);
            int newOffset = FindOffsetForInsert(ref key);

            var entry = new RomFsEntry<T>();
            entry.Next = Buckets[bucket];
            entry.Parent = key.Parent;
            entry.KeyLength = key.Name.Length;
            entry.Value = value;

            SetEntryInternal(newOffset, ref entry, ref key.Name);

            Buckets[bucket] = newOffset;
            _count++;
            return newOffset;
        }

        private int FindOffsetForInsert(ref RomEntryKey key)
        {
            int bytesNeeded = Util.AlignUp(_sizeOfEntry + key.Name.Length, 4);

            if (_length + bytesNeeded > _capacity)
            {
                EnsureCapacityBytes(_length + bytesNeeded);
            }

            int offset = _length;
            _length += bytesNeeded;

            return offset;
        }

        private int FindEntry(ref RomEntryKey key)
        {
            uint hashCode = key.GetRomHashCode();
            int index = (int)(hashCode % Buckets.Length);
            int i = Buckets[index];

            while (i != -1)
            {
                GetEntryInternal(i, out RomFsEntry<T> entry, out ReadOnlySpan<byte> name);

                if (key.Parent == entry.Parent && key.Name.SequenceEqual(name))
                {
                    break;
                }

                i = entry.Next;
            }

            return i;
        }

        private void GetEntryInternal(int offset, out RomFsEntry<T> outEntry)
        {
            outEntry = MemoryMarshal.Read<RomFsEntry<T>>(Entries.AsSpan(offset));
        }

        private void GetEntryInternal(int offset, out RomFsEntry<T> outEntry, out ReadOnlySpan<byte> entryName)
        {
            GetEntryInternal(offset, out outEntry);

            if (outEntry.KeyLength > 0x300)
            {
                throw new InvalidDataException("Rom entry name is too long.");
            }

            entryName = Entries.AsSpan(offset + _sizeOfEntry, outEntry.KeyLength);
        }

        private void SetEntryInternal(int offset, ref RomFsEntry<T> entry)
        {
            MemoryMarshal.Write(Entries.AsSpan(offset), ref entry);
        }

        private void SetEntryInternal(int offset, ref RomFsEntry<T> entry, ref ReadOnlySpan<byte> entryName)
        {
            MemoryMarshal.Write(Entries.AsSpan(offset), ref entry);

            entryName.CopyTo(Entries.AsSpan(offset + _sizeOfEntry, entry.KeyLength));
        }

        private void EnsureCapacityBytes(int value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (value <= _capacity) return;

            long newCapacity = Math.Max(value, 256);
            newCapacity = Math.Max(newCapacity, _capacity * 2);

            SetCapacity((int)Math.Min(newCapacity, int.MaxValue));
        }

        private void SetCapacity(int value)
        {
            if (value < _length)
                throw new ArgumentOutOfRangeException(nameof(value), "Capacity is smaller than the current length.");

            if (value != _capacity)
            {
                var newBuffer = new byte[value];
                Buffer.BlockCopy(Entries, 0, newBuffer, 0, _length);

                Entries = newBuffer;
                _capacity = value;
            }
        }

        public int CountEntries()
        {
            int count = 0;
            int nextStructOffset = (sizeof(int) + Marshal.SizeOf<T>()) / 4;
            Span<int> data = MemoryMarshal.Cast<byte, int>(Entries.AsSpan());

            for (int i = 0; i < Buckets.Length; i++)
            {
                int next = Buckets[i];

                while (next != -1)
                {
                    next = data[next / 4 + nextStructOffset];
                    count++;
                }
            }

            return count;
        }

        public void Resize(int newSize)
        {
            var newBuckets = new int[newSize];
            newBuckets.AsSpan().Fill(-1);

            List<int> offsets = GetEntryOffsets();
            var key = new RomEntryKey();

            for (int i = 0; i < offsets.Count; i++)
            {
                ref RomFsEntry<T> entry = ref GetEntryReference(offsets[i], out key.Name);
                key.Parent = entry.Parent;

                uint hashCode = key.GetRomHashCode();
                int bucket = (int)(hashCode % newSize);

                entry.Next = newBuckets[bucket];
                newBuckets[bucket] = offsets[i];
            }

            Buckets = newBuckets;
        }

        private ref RomFsEntry<T> GetEntryReference(int offset, out ReadOnlySpan<byte> name)
        {
            ref RomFsEntry<T> entry = ref MemoryMarshal.Cast<byte, RomFsEntry<T>>(Entries.AsSpan(offset))[0];

            name = Entries.AsSpan(offset + _sizeOfEntry, entry.KeyLength);
            return ref entry;
        }

        private List<int> GetEntryOffsets()
        {
            var offsets = new List<int>(_count);

            int nextStructOffset = (sizeof(int) + Marshal.SizeOf<T>()) / 4;
            Span<int> data = MemoryMarshal.Cast<byte, int>(Entries.AsSpan());

            for (int i = 0; i < Buckets.Length; i++)
            {
                int next = Buckets[i];

                while (next != -1)
                {
                    offsets.Add(next);
                    next = data[next / 4 + nextStructOffset];
                }
            }

            offsets.Sort();
            return offsets;
        }

        private int EstimateEntryTableSize(int count) => (_sizeOfEntry + 0x10) * count; // Estimate 0x10 bytes per name
    }
}
