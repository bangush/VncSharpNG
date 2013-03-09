using System;
using System.Collections.Generic;
using System.Text;

namespace VncSharp
{
    public class HexConverter
    {
        public static String ToHex(byte[] input)
        {
            var hex = BitConverter.ToString(input);
            hex = hex.Replace("-", " ");
            return hex;
        }

        public static String ToHex(byte input)
        {
            return ToHex(new[] { input });
        }

        public static String ToHex(ushort input)
        {
            var bytes = BitConverter.GetBytes(input);
            return ToHex(bytes);
        }

        public static String ToHex(uint input)
        {
            var bytes = BitConverter.GetBytes(input);
            return ToHex(bytes);
        }

        public static String ToHex(uint[] input)
        {
            var bytes = new List<byte>();
            foreach (var value in input)
            {
                bytes.AddRange(BitConverter.GetBytes(value));
            }
            return ToHex(bytes.ToArray());
        }
    }
}
