using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace VncSharp
{
    public class DebugBinaryWriter : BinaryWriter
    {
        #region "Overrides"
        public DebugBinaryWriter(Stream input) : base(input) { }
        public DebugBinaryWriter(Stream input, Encoding encoding) : base(input, encoding) { }

        public new virtual void Close()
        {
            Debug.Print("BinaryWriter.Close()");
            base.Close();
        }

        new public virtual void Write(byte value)
        {
            Debug.Print("> " + HexConverter.ToHex(value));
            base.Write(value);
        }

        new public virtual void Write(byte[] buffer)
        {
            Debug.Print("> " + HexConverter.ToHex(buffer));
            base.Write(buffer);
        }

        new public virtual void Write(ushort value)
        {
            Debug.Print("> " + HexConverter.ToHex(value));
            base.Write(value);
        }

        new public virtual void Write(uint value)
        {
            Debug.Print("> " + HexConverter.ToHex(value));
            base.Write(value);
        }

        new public virtual void Write(byte[] buffer, int index, int count)
        {
            var part = new byte[count];
            for (int x = 0; x < count; x++)
            {
                part[x] = buffer[x + index];
            }

            Debug.Print("> " + HexConverter.ToHex(part));
            base.Write(buffer, index, count);
        }

        new public virtual void Flush()
        {
            Debug.Print("BinaryWriter.Flush()");
            base.Flush();
        }
        #endregion
    }
}
