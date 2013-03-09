using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace VncSharp
{
    public class DebugBinaryReader : BinaryReader
    {
        public DebugBinaryReader(Stream input) : base(input) { }
        public DebugBinaryReader(Stream input, Encoding encoding) : base(input, encoding) { }

        new public virtual void Close()
        {
            Debug.Print("BinaryReader.Close()");
            base.Close();
        }

        new public virtual byte ReadByte()
        {
            var value = base.ReadByte();
            Debug.Print("< " + HexConverter.ToHex(value));
            return value;
        }

        new public virtual byte[] ReadBytes(int count)
        {
            var value = base.ReadBytes(count);
            Debug.Print("< " + HexConverter.ToHex(value));
            return value;
        }

        new public virtual ushort ReadUInt16()
        {
            var value = base.ReadUInt16();
            Debug.Print("< " + HexConverter.ToHex(value));
            return value;
        }

        new public virtual uint ReadUInt32()
        {
            var value = base.ReadUInt32();
            Debug.Print("< " + HexConverter.ToHex(value));
            return value;
        }
    }
}
