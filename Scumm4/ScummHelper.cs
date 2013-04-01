﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Scumm4
{
    internal static class ScummHelper
    {
        public static int NewDirToOldDir(int dir)
        {
            if (dir >= 71 && dir <= 109)
                return 1;
            if (dir >= 109 && dir <= 251)
                return 2;
            if (dir >= 251 && dir <= 289)
                return 0;
            return 3;
        }

        public static int RevBitMask(int x)
        {
            return (0x80 >> (x));
        }

        public static void AssertRange(int min, int value, int max, string desc)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException("value", string.Format("{0} {1} is out of bounds ({2},{3})", desc, value, min, max));
            }
        }

        public static uint[] ReadUInt32s(this BinaryReader reader, int count)
        {
            uint[] values = new uint[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = reader.ReadUInt32();
            }
            return values;
        }

        public static int[] ReadInt32s(this BinaryReader reader, int count)
        {
            int[] values = new int[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = reader.ReadInt32();
            }
            return values;
        }

        public static short[] ReadInt16s(this BinaryReader reader, int count)
        {
            short[] values = new short[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = reader.ReadInt16();
            }
            return values;
        }

        public static ushort[] ReadUInt16s(this BinaryReader reader, int count)
        {
            ushort[] values = new ushort[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = reader.ReadUInt16();
            }
            return values;
        }

        public static int[][] ReadMatrixUInt16(this BinaryReader reader, int count1, int count2)
        {
            int[][] values = new int[count1][];

            for (int i = 0; i < count1; i++)
            {
                values[i] = new int[count2];
                for (int j = 0; j < count2; j++)
                {
                    values[i][j] = reader.ReadUInt16();
                }
            }
            return values;
        }

        internal static int[][] ReadMatrixInt32(this BinaryReader reader, int count1, int count2)
        {
            int[][] values = new int[count1][];

            for (int i = 0; i < count1; i++)
            {
                values[i] = new int[count2];
                for (int j = 0; j < count2; j++)
                {
                    values[i][j] = reader.ReadInt32();
                }
            }
            return values;
        }

        internal static byte[][] ReadMatrixBytes(this BinaryReader reader, int count1, int count2)
        {
            byte[][] values = new byte[count1][];

            for (int i = 0; i < count1; i++)
            {
                values[i] = new byte[count2];
                for (int j = 0; j < count2; j++)
                {
                    values[i][j] = reader.ReadByte();
                }
            }
            return values;
        }

        public static ushort SwapBytes(ushort value)
        {
            return (ushort)((value & 0xFFU) << 8 | (value & 0xFF00U) >> 8);
        }

        public static uint SwapBytes(uint value)
        {
            return (value & 0x000000FFU) << 24 | (value & 0x0000FF00U) << 8 | (value & 0x00FF0000U) >> 8 | (value & 0xFF000000U) >> 24;
        }

        public static uint MakeTag(char a0, char a1, char a2, char a3)
        {
            return ((uint)((a3) | ((a2) << 8) | ((a1) << 16) | ((a0) << 24)));
        }

    }
}
