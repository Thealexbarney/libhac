﻿using System;
using System.Text;
using LibHac;
using LibHac.IO;

namespace hactoolnet
{
    internal static class Print
    {
        public static void PrintItem(StringBuilder sb, int colLen, string prefix, object data)
        {
            if (data is byte[] byteData)
            {
                sb.MemDump(prefix.PadRight(colLen), byteData);
            }
            else
            {
                sb.AppendLine(prefix.PadRight(colLen) + data);
            }
        }

        public static string GetValidityString(this Validity validity)
        {
            switch (validity)
            {
                case Validity.Invalid: return " (FAIL)";
                case Validity.Valid: return " (GOOD)";
                default: return string.Empty;
            }
        }

        public static void PrintIvfcHash(StringBuilder sb, int colLen, int indentSize, IvfcHeader ivfcInfo, IntegrityStorageType type)
        {
            string prefix = new string(' ', indentSize);
            string prefix2 = new string(' ', indentSize + 4);

            if (type == IntegrityStorageType.RomFs)
                PrintItem(sb, colLen, $"{prefix}Master Hash{ivfcInfo.LevelHeaders[0].HashValidity.GetValidityString()}:", ivfcInfo.MasterHash);

            PrintItem(sb, colLen, $"{prefix}Magic:", ivfcInfo.Magic);
            PrintItem(sb, colLen, $"{prefix}Version:", ivfcInfo.Version);

            if (type == IntegrityStorageType.Save)
                PrintItem(sb, colLen, $"{prefix}Salt Seed:", ivfcInfo.SaltSource);

            int levelCount = Math.Max(ivfcInfo.NumLevels - 1, 0);
            if (type == IntegrityStorageType.Save) levelCount = 4;

            int offsetLen = type == IntegrityStorageType.Save ? 16 : 12;

            for (int i = 0; i < levelCount; i++)
            {
                IvfcLevelHeader level = ivfcInfo.LevelHeaders[i];
                long hashOffset = 0;

                if (i != 0)
                {
                    hashOffset = ivfcInfo.LevelHeaders[i - 1].Offset;
                }

                sb.AppendLine($"{prefix}Level {i}{level.HashValidity.GetValidityString()}:");
                PrintItem(sb, colLen, $"{prefix2}Data Offset:", $"0x{level.Offset.ToString($"x{offsetLen}")}");
                PrintItem(sb, colLen, $"{prefix2}Data Size:", $"0x{level.Size.ToString($"x{offsetLen}")}");
                PrintItem(sb, colLen, $"{prefix2}Hash Offset:", $"0x{hashOffset.ToString($"x{offsetLen}")}");
                PrintItem(sb, colLen, $"{prefix2}Hash BlockSize:", $"0x{1 << level.BlockSizePower:x8}");
            }
        }
    }
}
