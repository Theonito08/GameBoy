using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameBoyEmu.Cartrige
{
    public interface IMBC
    {
        byte Read(ushort address);
        void Write(ushort address, byte value);
        void Save(BinaryWriter writer);
        void Load(BinaryReader reader);
    }
}
